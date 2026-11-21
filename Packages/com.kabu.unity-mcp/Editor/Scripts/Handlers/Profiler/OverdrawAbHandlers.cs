using System;
using System.Collections.Generic;
using System.Linq;
using KabuUnityMcp.Editor.Handlers.SystemTools;
using KabuUnityMcp.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// unity_perf_get_overdraw：Play → Game Additive 整屏层数 → 候选 A/B（关 Renderer 测 avgLayersDrop）。
    /// 与 capture / renderer_sweep 互斥 busy。
    /// </summary>
    internal static class OverdrawAbHandlers
    {
        private static bool busy;
        private static string pendingRequestId;
        private static McpWebSocketClient pendingClient;
        private static Action<bool, string> localOnDone;

        private static Phase phase;
        private static int waitFrames;
        private static bool updateHooked;
        private static bool autoEnteredPlay;
        private static bool waitingForPlay;
        private static double deadline;

        private static string rootPath;
        private static int topN;
        private static int candidateLimit;
        private static int settleFrames;

        private static List<OverdrawOps.AbCandidate> candidates;
        private static int candidateIndex;
        private static OverdrawOps.OverdrawSample baseline;
        private static readonly List<OverdrawOps.OverdrawTopRow> rows = new List<OverdrawOps.OverdrawTopRow>();
        private static readonly List<string> warnings = new List<string>();
        private static readonly Dictionary<int, bool> savedEnabled = new Dictionary<int, bool>();
        private static int scannedRenderers;
        private static OverdrawOps.MaterialProxyStats materialProxy;

        public static event Action Changed;

        private enum Phase
        {
            None,
            WaitingForPlay,
            SettleWait,
            SampleBaseline,
            ApplyOff,
            SettleAfterOff,
            SampleWithout,
            Restore,
            Finish
        }

        public static bool IsBusy => busy;

        public static void Capture(string requestId, JToken parameters, McpWebSocketClient client)
        {
            if (busy || PerfCaptureHandlers.IsBusy || PerfRendererSweepHandlers.IsBusy)
            {
                _ = client.SendErrorAsync(requestId, -32000, "perf capture/sweep/overdraw 进行中，请稍后重试。");
                return;
            }

            try
            {
                Begin(parameters, requestId, client, null);
            }
            catch (Exception ex)
            {
                ClearPending(restoreGuard: true);
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        /// <summary>Editor 测试页：同源异步，回调 (ok, jsonOrError)。</summary>
        public static void CaptureLocal(
            Action<bool, string> onDone,
            int topNParam = OverdrawOps.DefaultTopN,
            int candidateLimitParam = OverdrawOps.DefaultCandidateLimit,
            int settleFramesParam = OverdrawOps.DefaultSettleFrames,
            string rootPathParam = null)
        {
            if (onDone == null)
                throw new ArgumentNullException(nameof(onDone));

            if (busy || PerfCaptureHandlers.IsBusy || PerfRendererSweepHandlers.IsBusy)
            {
                onDone(false, "perf capture/sweep/overdraw 进行中，请稍后重试。");
                return;
            }

            try
            {
                var parameters = new JObject
                {
                    ["topN"] = topNParam,
                    ["candidateLimit"] = candidateLimitParam,
                    ["settleFrames"] = settleFramesParam,
                    ["rootPath"] = rootPathParam
                };
                Begin(parameters, null, null, onDone);
            }
            catch (Exception ex)
            {
                ClearPending(restoreGuard: true);
                onDone(false, ex.Message);
            }
        }

        private static void Begin(
            JToken parameters,
            string requestId,
            McpWebSocketClient client,
            Action<bool, string> onDone)
        {
            rootPath = parameters?["rootPath"]?.Value<string>();
            topN = parameters?["topN"]?.Value<int?>() ?? OverdrawOps.DefaultTopN;
            candidateLimit = parameters?["candidateLimit"]?.Value<int?>() ?? OverdrawOps.DefaultCandidateLimit;
            settleFrames = parameters?["settleFrames"]?.Value<int?>() ?? OverdrawOps.DefaultSettleFrames;

            topN = Math.Clamp(topN <= 0 ? OverdrawOps.DefaultTopN : topN, 1, OverdrawOps.MaxTopN);
            candidateLimit = Math.Clamp(
                candidateLimit <= 0 ? OverdrawOps.DefaultCandidateLimit : candidateLimit,
                1,
                OverdrawOps.MaxCandidateLimit);
            settleFrames = Math.Clamp(settleFrames, 4, 60);

            var scene = SceneManager.GetActiveScene();
            materialProxy = OverdrawOps.BuildMaterialProxy(scene);
            candidates = OverdrawOps.BuildAbCandidates(scene, rootPath, candidateLimit, out scannedRenderers);
            if (candidates.Count == 0)
                throw new InvalidOperationException("无 A/B 候选（检查 Play 场景是否有激活 Renderer / rootPath）。");

            busy = true;
            pendingRequestId = requestId;
            pendingClient = client;
            localOnDone = onDone;
            candidateIndex = -1;
            rows.Clear();
            warnings.Clear();
            savedEnabled.Clear();
            autoEnteredPlay = false;
            waitingForPlay = false;
            baseline = default;
            deadline = EditorApplication.timeSinceStartup + 600.0;

            warnings.Add(
                $"candidates={candidates.Count}, scanned={scannedRenderers}, topN={topN}, settleFrames={settleFrames}");

            EnsureUpdateHook();
            RaiseChanged();
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

                // Domain Reload 后静态字段可能还在，但场景 Renderer 引用需重建
                var scene = SceneManager.GetActiveScene();
                materialProxy = OverdrawOps.BuildMaterialProxy(scene);
                candidates = OverdrawOps.BuildAbCandidates(scene, rootPath, candidateLimit, out scannedRenderers);
                if (candidates.Count == 0)
                {
                    Fail("进 Play 后无 A/B 候选。");
                    return;
                }

                phase = Phase.SettleWait;
                waitFrames = settleFrames;
                PerfCaptureOps.FocusGameView(out _);
                QueuePlayerLoop();
                RaiseChanged();
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
                Fail("overdraw A/B 超时（600s）。");
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
                        phase = Phase.SampleBaseline;
                        break;
                    case Phase.SampleBaseline:
                        DoSampleBaseline();
                        break;
                    case Phase.ApplyOff:
                        DoApplyOff();
                        break;
                    case Phase.SettleAfterOff:
                        phase = Phase.SampleWithout;
                        break;
                    case Phase.SampleWithout:
                        DoSampleWithout();
                        break;
                    case Phase.Restore:
                        DoRestoreAndNext();
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

        private static void DoSampleBaseline()
        {
            PerfCaptureOps.FocusGameView(out _);
            if (!OverdrawOps.TrySampleGameOverdraw(out baseline, out var error))
            {
                Fail("baseline 采样失败：" + error);
                return;
            }

            if (baseline.avgOverdraw <= 0 && baseline.drawnRenderers <= 0)
                warnings.Add("baseline 层数≈0；A/B drop 可能无意义。");

            candidateIndex = 0;
            phase = Phase.ApplyOff;
            waitFrames = 0;
            RaiseChanged();
        }

        private static void DoApplyOff()
        {
            if (candidateIndex >= candidates.Count)
            {
                phase = Phase.Finish;
                waitFrames = 0;
                return;
            }

            var cand = candidates[candidateIndex];
            savedEnabled.Clear();
            foreach (var r in cand.Renderers)
            {
                if (!r) continue;
                var id = r.GetInstanceID();
                if (!savedEnabled.ContainsKey(id))
                    savedEnabled[id] = r.enabled;
                r.enabled = false;
            }

            phase = Phase.SettleAfterOff;
            waitFrames = settleFrames;
            QueuePlayerLoop();
        }

        private static void DoSampleWithout()
        {
            if (!OverdrawOps.TrySampleGameOverdraw(out var without, out var error))
            {
                warnings.Add($"候选#{candidateIndex} 采样失败：{error}，跳过");
                phase = Phase.Restore;
                waitFrames = 0;
                return;
            }

            var cand = candidates[candidateIndex];
            rows.Add(OverdrawOps.BuildAbRow(cand, baseline, without));
            phase = Phase.Restore;
            waitFrames = 0;
        }

        private static void DoRestoreAndNext()
        {
            var cand = candidates[candidateIndex];
            foreach (var r in cand.Renderers)
            {
                if (!r) continue;
                var id = r.GetInstanceID();
                r.enabled = savedEnabled.TryGetValue(id, out var prev) ? prev : true;
            }

            savedEnabled.Clear();
            candidateIndex++;
            if (candidateIndex >= candidates.Count)
            {
                phase = Phase.Finish;
                waitFrames = Math.Max(2, settleFrames / 2);
            }
            else
            {
                phase = Phase.ApplyOff;
                waitFrames = Math.Max(2, settleFrames / 2);
            }

            QueuePlayerLoop();
            RaiseChanged();
        }

        private static void FinishSuccess()
        {
            RestoreAllSaved();
            var top = OverdrawOps.RankAbResults(rows, topN);
            var payload = OverdrawOps.BuildFinalPayload(
                baseline,
                top,
                materialProxy,
                topN,
                candidateLimit,
                settleFrames,
                scannedRenderers,
                candidates?.Count ?? 0,
                rootPath,
                autoEnteredPlay,
                warnings);

            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            var client = pendingClient;
            var requestId = pendingRequestId;
            var cb = localOnDone;
            var ok = top.Count > 0 || baseline.avgOverdraw > 0;
            ClearPending(restoreGuard: false);

            if (cb != null)
                cb(ok, json);
            else if (client != null && requestId != null)
                _ = client.SendResponseAsync(requestId, payload);

            RaiseChanged();
        }

        private static void Fail(string message)
        {
            RestoreAllSaved();
            var client = pendingClient;
            var requestId = pendingRequestId;
            var cb = localOnDone;
            ClearPending(restoreGuard: false);

            if (cb != null)
                cb(false, message);
            else if (client != null && requestId != null)
                _ = client.SendErrorAsync(requestId, -32000, message);

            Debug.LogError("[kabu-unity-mcp] overdraw A/B 失败：" + message);
            RaiseChanged();
        }

        private static void RestoreAllSaved()
        {
            if (candidates == null) return;
            foreach (var cand in candidates)
            {
                if (cand?.Renderers == null) continue;
                foreach (var r in cand.Renderers)
                {
                    if (!r) continue;
                    // 若仍在 savedEnabled 中说明中断于 off 态
                    if (savedEnabled.TryGetValue(r.GetInstanceID(), out var prev))
                        r.enabled = prev;
                    else if (!r.enabled)
                        r.enabled = true;
                }
            }

            savedEnabled.Clear();
        }

        private static void ClearPending(bool restoreGuard)
        {
            if (restoreGuard)
                RestoreAllSaved();

            if (waitingForPlay)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                waitingForPlay = false;
            }

            RemoveUpdateHook();
            busy = false;
            pendingRequestId = null;
            pendingClient = null;
            localOnDone = null;
            phase = Phase.None;
            candidates = null;
            candidateIndex = -1;
            rows.Clear();
            warnings.Clear();
        }

        private static void QueuePlayerLoop()
        {
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { /* ignore */ }
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); } catch { /* ignore */ }
        }
    }
}
