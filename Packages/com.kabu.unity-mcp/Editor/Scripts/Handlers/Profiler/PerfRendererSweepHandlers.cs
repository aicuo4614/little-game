using System;
using System.Collections.Generic;
using System.Linq;
using KabuUnityMcp.Editor.Handlers.SystemTools;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// unity_perf_renderer_sweep：Play 内 SetActive 关闭一批 Renderer 宿主 → 等稳 → 采 basics → 恢复，循环最多 batchCount 次。
    /// 不走 Undo（Play 下 Undo 会失败）；临时改动默认每批后恢复。
    /// 采样要求连续 stableReads 次 Batches/DrawCalls/SetPass 一致，避免大批量改层级后的脏帧。
    /// </summary>
    internal static class PerfRendererSweepHandlers
    {
        private static bool busy;
        private static string pendingRequestId;
        private static McpWebSocketClient pendingClient;

        private static Phase phase;
        private static int waitFrames;
        private static int sampleAttempt;
        private static bool updateHooked;
        private static bool autoEnteredPlay;
        private static bool waitingForPlay;
        private static double deadline;

        private static string rootPath;
        private static string groupBy;
        private static string targetMode;
        private static int batchCount;
        private static int topN;
        private static int settleFrames;
        private static int stableReads;
        private static bool includeInactive;
        private static bool restoreAtEnd;
        private static bool exitPlayWhenDone;

        private static List<PerfRendererSweepOps.BatchGroup> batches;
        private static int batchIndex;
        private static PerfRendererSweepOps.Metrics baseline;
        private static readonly List<object> rows = new List<object>();
        private static readonly List<string> warnings = new List<string>();
        private static readonly Dictionary<int, bool> savedActive = new Dictionary<int, bool>();

        private static List<ProfilerRecorder> pendingRecorders;
        private static List<string> pendingNames;
        private static bool capturingBaseline;

        private static PerfRendererSweepOps.Metrics lastStableCandidate;
        private static int consecutiveStable;
        private static int lastToggleObjectCount;

        private enum Phase
        {
            None,
            WaitingForPlay,
            SettleWait,
            SamplePrep,
            SampleWait,
            ApplyBatch,
            RestoreBatch,
            Finish
        }

        public static bool IsBusy => busy;

        public static void Sweep(string requestId, JToken parameters, McpWebSocketClient client)
        {
            if (busy || PerfCaptureHandlers.IsBusy || OverdrawAbHandlers.IsBusy)
            {
                _ = client.SendErrorAsync(requestId, -32000, "perf capture/sweep/overdraw 进行中，请稍后重试。");
                return;
            }

            try
            {
                Begin(parameters, requestId, client);
            }
            catch (Exception ex)
            {
                ClearPending(disposeRecorders: true, restoreGuard: true);
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        private static void Begin(JToken parameters, string requestId, McpWebSocketClient client)
        {
            rootPath = parameters?["rootPath"]?.ToString();
            groupBy = parameters?["groupBy"]?.ToString() ?? "sibling";
            targetMode = parameters?["target"]?.ToString() ?? "rendererGo";
            batchCount = parameters?["batchCount"]?.Value<int?>() ?? 10;
            topN = parameters?["topN"]?.Value<int?>() ?? 50;
            settleFrames = parameters?["settleFrames"]?.Value<int?>() ?? 16;
            stableReads = parameters?["stableReads"]?.Value<int?>() ?? 3;
            includeInactive = parameters?["includeInactive"]?.Value<bool?>() ?? false;
            restoreAtEnd = parameters?["restore"]?.Value<bool?>() ?? true;
            exitPlayWhenDone = parameters?["exitPlayWhenDone"]?.Value<bool?>() ?? false;

            batchCount = Math.Clamp(batchCount, 1, 50);
            topN = Math.Clamp(topN <= 0 ? 50 : topN, 1, 200);
            settleFrames = Math.Clamp(settleFrames, 8, 120);
            stableReads = Math.Clamp(stableReads, 2, 5);

            var targets = PerfRendererSweepOps.CollectTargets(rootPath, includeInactive, out var collectError);
            if (!string.IsNullOrEmpty(collectError) && targets.Count == 0)
            {
                _ = client.SendErrorAsync(requestId, -32000, collectError);
                return;
            }

            if (targets.Count == 0)
            {
                _ = client.SendErrorAsync(requestId, -32000, "未找到可扫的 Renderer（检查 rootPath / includeInactive）。");
                return;
            }

            batches = PerfRendererSweepOps.BuildBatches(targets, groupBy, targetMode, batchCount);
            if (batches.Count == 0)
            {
                _ = client.SendErrorAsync(requestId, -32000, "分组后无批次。");
                return;
            }

            busy = true;
            pendingRequestId = requestId;
            pendingClient = client;
            batchIndex = -1;
            baseline = null;
            rows.Clear();
            warnings.Clear();
            savedActive.Clear();
            capturingBaseline = true;
            autoEnteredPlay = false;
            waitingForPlay = false;
            lastToggleObjectCount = 0;
            ResetStableState();
            deadline = EditorApplication.timeSinceStartup + 900.0;

            if (!string.IsNullOrEmpty(collectError))
                warnings.Add(collectError);

            warnings.Add(
                $"candidates={targets.Count}, batches={batches.Count}, groupBy={groupBy}, target={targetMode}, " +
                $"settleFrames={settleFrames}, stableReads={stableReads}");

            EnsureUpdateHook();
            PerfCaptureOps.FocusGameView(out _);

            if (!EditorApplication.isPlaying)
            {
                EnterPlayModeOptionsGuard.ApplyDisableDomainReload();
                waitingForPlay = true;
                autoEnteredPlay = true;
                phase = Phase.WaitingForPlay;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                EditorApplication.isPlaying = true;
            }
            else
            {
                phase = Phase.SettleWait;
                waitFrames = settleFrames;
                QueuePlayerLoop();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!busy || !waitingForPlay)
                return;

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                waitingForPlay = false;
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                phase = Phase.SettleWait;
                waitFrames = settleFrames;
                PerfCaptureOps.FocusGameView(out _);
                QueuePlayerLoop();
            }
        }

        private static void EnsureUpdateHook()
        {
            if (updateHooked)
                return;
            EditorApplication.update += OnUpdate;
            updateHooked = true;
        }

        private static void RemoveUpdateHook()
        {
            if (!updateHooked)
                return;
            EditorApplication.update -= OnUpdate;
            updateHooked = false;
        }

        private static void OnUpdate()
        {
            if (!busy)
                return;

            if (EditorApplication.timeSinceStartup > deadline)
            {
                Fail("renderer sweep 超时（900s）。");
                return;
            }

            if (waitFrames > 0)
            {
                waitFrames--;
                QueuePlayerLoop();
                return;
            }

            try
            {
                switch (phase)
                {
                    case Phase.WaitingForPlay:
                        break;
                    case Phase.SettleWait:
                    case Phase.SamplePrep:
                        BeginSample();
                        break;
                    case Phase.SampleWait:
                        ReadSampleAndContinue();
                        break;
                    case Phase.ApplyBatch:
                        ApplyCurrentBatchOff();
                        break;
                    case Phase.RestoreBatch:
                        RestoreCurrentBatch();
                        break;
                    case Phase.Finish:
                        FinishSuccess();
                        break;
                }
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        private static void BeginSample()
        {
            sampleAttempt++;
            if (!ProfilerCounterOps.TryBeginCapture(
                    "Render",
                    PerfRendererSweepOps.BasicsCounterNames,
                    out pendingRecorders,
                    out pendingNames,
                    out var error))
            {
                Fail("启动计数器失败：" + error);
                return;
            }

            phase = Phase.SampleWait;
            waitFrames = 4 + Math.Min(sampleAttempt, 6);
            QueuePlayerLoop();
        }

        private static void ReadSampleAndContinue()
        {
            var map = new Dictionary<string, long>(StringComparer.Ordinal);
            try
            {
                for (var i = 0; i < pendingRecorders.Count; i++)
                {
                    var recorder = pendingRecorders[i];
                    var name = pendingNames[i];
                    long value = 0;
                    if (recorder.Valid)
                    {
                        var current = recorder.CurrentValue;
                        var count = recorder.Count;
                        long newest = 0;
                        if (count > 0)
                            newest = recorder.GetSample(count - 1).Value;
                        value = current != 0 ? current : newest;
                    }

                    map[name] = value;
                }
            }
            finally
            {
                ProfilerCounterOps.DisposeAll(pendingRecorders);
                pendingRecorders = null;
                pendingNames = null;
            }

            var metrics = PerfRendererSweepOps.FromCounterMap(map);
            var allZero = metrics.Batches == 0 && metrics.DrawCalls == 0 && metrics.SetPassCalls == 0;
            if (allZero)
            {
                if (sampleAttempt < 8)
                {
                    warnings.Add($"采样第 {sampleAttempt} 次全 0，加长等待后重试。");
                    ResetStableState();
                    PerfCaptureOps.FocusGameView(out _);
                    phase = Phase.SamplePrep;
                    waitFrames = Math.Max(settleFrames, 12) + sampleAttempt * 4;
                    QueuePlayerLoop();
                    return;
                }

                warnings.Add("多次采样仍全 0，继续用该值（可能无意义）。");
            }

            if (!TryAcceptStable(metrics, out var accepted))
            {
                phase = Phase.SamplePrep;
                waitFrames = 6 + consecutiveStable;
                QueuePlayerLoop();
                return;
            }

            AcceptMetrics(accepted);
        }

        private static bool TryAcceptStable(
            PerfRendererSweepOps.Metrics metrics,
            out PerfRendererSweepOps.Metrics accepted)
        {
            accepted = null;
            if (lastStableCandidate != null && MetricsKeyEqual(lastStableCandidate, metrics))
            {
                consecutiveStable++;
            }
            else
            {
                lastStableCandidate = metrics;
                consecutiveStable = 1;
            }

            if (consecutiveStable < stableReads)
                return false;

            // 相对 baseline 异常大跌：再采一轮（仅非 baseline 阶段）
            if (!capturingBaseline && baseline != null && baseline.Batches > 0)
            {
                var dropRatio = (baseline.Batches - metrics.Batches) / (double)baseline.Batches;
                // 单批关掉后掉超过 70% 且开关物体很多时，更可能是脏帧；要求再稳一次
                if (dropRatio > 0.7 && lastToggleObjectCount >= 50 && sampleAttempt < 12)
                {
                    warnings.Add(
                        $"batch#{batchIndex} Batches 从 {baseline.Batches}→{metrics.Batches}（降幅 {dropRatio:P0}），" +
                        $"疑似未稳，继续等待（objects={lastToggleObjectCount}）。");
                    ResetStableState();
                    return false;
                }
            }

            accepted = metrics;
            return true;
        }

        private static void AcceptMetrics(PerfRendererSweepOps.Metrics metrics)
        {
            ResetStableState();
            sampleAttempt = 0;

            if (capturingBaseline)
            {
                baseline = metrics;
                capturingBaseline = false;
                if (metrics.Batches == 0)
                    warnings.Add("baseline 仍为 0；后续 delta 可能无意义。");
                batchIndex = 0;
                phase = Phase.ApplyBatch;
                waitFrames = 0;
                return;
            }

            var batch = batches[batchIndex];
            rows.Add(PerfRendererSweepOps.BuildRow(batch, baseline, metrics));
            phase = Phase.RestoreBatch;
            waitFrames = 0;
        }

        private static void ApplyCurrentBatchOff()
        {
            if (batchIndex >= batches.Count)
            {
                phase = Phase.Finish;
                waitFrames = 0;
                return;
            }

            var batch = batches[batchIndex];
            lastToggleObjectCount = batch.Items.Count;
            foreach (var item in batch.Items)
            {
                var go = item.Go;
                if (go == null)
                    continue;
                var id = go.GetInstanceID();
                if (!savedActive.ContainsKey(id))
                    savedActive[id] = go.activeSelf;
                go.SetActive(false);
            }

            ResetStableState();
            sampleAttempt = 0;
            phase = Phase.SamplePrep;
            waitFrames = ComputeWaitAfterToggle(lastToggleObjectCount);
            QueuePlayerLoop();
        }

        private static void RestoreCurrentBatch()
        {
            var batch = batches[batchIndex];
            var count = batch.Items.Count;
            foreach (var item in batch.Items)
            {
                var go = item.Go;
                if (go == null)
                    continue;
                var id = go.GetInstanceID();
                var desired = savedActive.TryGetValue(id, out var prev) ? prev : true;
                go.SetActive(desired);
            }

            batchIndex++;
            if (batchIndex >= batches.Count)
            {
                phase = Phase.Finish;
                waitFrames = ComputeWaitAfterToggle(count);
            }
            else
            {
                // 恢复后再等稳，避免下一批在未恢复完的渲染上开关
                phase = Phase.ApplyBatch;
                waitFrames = ComputeWaitAfterToggle(count);
            }

            QueuePlayerLoop();
        }

        /// <summary>
        /// 按改动物体数量加长等待：上千个 SetActive 后需要更多帧才能出稳定渲染计数。
        /// </summary>
        private static int ComputeWaitAfterToggle(int objectCount)
        {
            var extra = 0;
            if (objectCount > 0)
                extra = Math.Min(90, objectCount / 15); // 1052 → ~70
            return Math.Clamp(settleFrames + extra, settleFrames, 160);
        }

        private static bool MetricsKeyEqual(
            PerfRendererSweepOps.Metrics a,
            PerfRendererSweepOps.Metrics b)
        {
            return a.Batches == b.Batches
                   && a.DrawCalls == b.DrawCalls
                   && a.SetPassCalls == b.SetPassCalls;
        }

        private static void ResetStableState()
        {
            lastStableCandidate = null;
            consecutiveStable = 0;
        }

        private static void FinishSuccess()
        {
            if (restoreAtEnd)
                RestoreAllSaved();

            var sorted = rows
                .OrderByDescending(r =>
                {
                    var jo = JObject.FromObject(r);
                    return jo["delta"]?["batchesDrop"]?.Value<long>() ?? 0;
                })
                .Take(topN)
                .ToList();

            var payload = new
            {
                schema = PerfRendererSweepOps.Schema,
                ok = baseline != null && baseline.Batches > 0,
                play = new
                {
                    isPlaying = EditorApplication.isPlaying,
                    autoEnteredPlay
                },
                paramsUsed = new
                {
                    rootPath,
                    groupBy,
                    target = targetMode,
                    batchCount,
                    topN,
                    settleFrames,
                    stableReads,
                    includeInactive,
                    restore = restoreAtEnd
                },
                baseline = baseline == null ? null : new
                {
                    batches = baseline.Batches,
                    drawCalls = baseline.DrawCalls,
                    setPassCalls = baseline.SetPassCalls,
                    triangles = baseline.Triangles,
                    vertices = baseline.Vertices
                },
                batchTotal = batches.Count,
                returned = sorted.Count,
                top = sorted,
                warnings
            };

            var requestId = pendingRequestId;
            var client = pendingClient;

            if (exitPlayWhenDone && autoEnteredPlay && EditorApplication.isPlaying)
            {
                ClearPending(disposeRecorders: true, restoreGuard: true);
                EditorApplication.isPlaying = false;
            }
            else
            {
                ClearPending(disposeRecorders: true, restoreGuard: true);
            }

            _ = client.SendResponseAsync(requestId, payload);
        }

        private static void RestoreAllSaved()
        {
            foreach (var kv in savedActive)
            {
                var obj = EditorUtility.InstanceIDToObject(kv.Key) as GameObject;
                if (obj != null)
                    obj.SetActive(kv.Value);
            }
        }

        private static void Fail(string message)
        {
            try { RestoreAllSaved(); } catch { /* ignore */ }

            var requestId = pendingRequestId;
            var client = pendingClient;
            ClearPending(disposeRecorders: true, restoreGuard: true);
            if (client != null && !string.IsNullOrEmpty(requestId))
                _ = client.SendErrorAsync(requestId, -32000, message);
        }

        private static void ClearPending(bool disposeRecorders, bool restoreGuard)
        {
            if (waitingForPlay)
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            if (disposeRecorders)
            {
                ProfilerCounterOps.DisposeAll(pendingRecorders);
                pendingRecorders = null;
                pendingNames = null;
            }

            if (restoreGuard)
                EnterPlayModeOptionsGuard.Restore();

            RemoveUpdateHook();
            busy = false;
            waitingForPlay = false;
            pendingRequestId = null;
            pendingClient = null;
            phase = Phase.None;
            waitFrames = 0;
            sampleAttempt = 0;
            batches = null;
            baseline = null;
            capturingBaseline = false;
            lastToggleObjectCount = 0;
            ResetStableState();
        }

        private static void QueuePlayerLoop()
        {
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { /* ignore */ }
        }
    }
}
