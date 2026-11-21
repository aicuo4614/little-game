using System;
using System.Collections.Generic;
using System.Linq;
using KabuUnityMcp.Editor.Handlers.SystemTools;
using KabuUnityMcp.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEditor;
using UProfiler = UnityEngine.Profiling.Profiler;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// unity_perf_capture_frame：异步编排
    /// focus Game → basics(Counters) → profile → FrameDebugger → overdraw占位 → 清理。
    /// 支持 MCP WebSocket 回包，或 Editor 窗口本地回调（JSON 字符串）。
    /// </summary>
    internal static class PerfCaptureHandlers
    {
        private static bool busy;
        private static string pendingRequestId;
        private static McpWebSocketClient pendingClient;
        private static Action<bool, string> localOnDone;
        private static Phase phase;
        private static int waitFrames;
        private static int basicsAttempt;

        private static HashSet<string> sections;
        private static int hierarchyTopN;
        private static int fdEventLimit;
        private static bool leavePaused;
        private static bool disableProfilerAfter;
        private static bool strictBasics;
        private static bool profilerWasEnabledBefore;
        private static bool gameViewFocused;
        private static bool frameDebuggerUsed;

        private static List<string> warnings;
        private static object basicsSection;
        private static object profileSection;
        private static object frameDebuggerSection;
        private static object overdrawSection;
        private static bool basicsOkFlag;
        private static bool profileOkFlag;
        private static bool fdOkFlag;

        private static List<ProfilerRecorder> pendingRecorders;
        private static List<string> pendingNames;

        private static bool waitingForPlay;
        private static bool autoEnteredPlay;
        private static int hierarchyLastAtWaitStart;
        private static int hierarchyWaitAttempts;

        private enum Phase
        {
            None,
            WaitingForPlay,
            PrepWait,
            CountersWait,
            AfterCounters,
            HierarchyWait,
            FdWait,
            AfterFd,
            Finish
        }

        public static bool IsBusy => busy;

        /// <summary>本地/异步状态变化（供 Server Window 刷新）。</summary>
        public static event Action Changed;

        public static void Capture(string requestId, JToken parameters, McpWebSocketClient client)
        {
            if (busy || PerfRendererSweepHandlers.IsBusy || OverdrawAbHandlers.IsBusy)
            {
                _ = client.SendErrorAsync(requestId, -32000, "perf capture/sweep/overdraw 进行中，请稍后重试。");
                return;
            }

            try
            {
                BeginCapture(parameters, requestId, client, null);
            }
            catch (Exception ex)
            {
                ClearPending(disposeRecorders: true);
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        /// <summary>
        /// Editor 窗口直接跑同一套编排；完成后回调 (overallOk, jsonOrError)。
        /// </summary>
        public static void CaptureLocal(
            Action<bool, string> onDone,
            bool strictBasicsParam = true,
            int hierarchyTopNParam = 30,
            int fdEventLimitParam = 0,
            bool leavePausedParam = false,
            bool disableProfilerAfterParam = false)
        {
            if (onDone == null)
                throw new ArgumentNullException(nameof(onDone));

            if (busy || PerfRendererSweepHandlers.IsBusy || OverdrawAbHandlers.IsBusy)
            {
                onDone(false, "perf capture/sweep/overdraw 进行中，请稍后重试。");
                return;
            }

            try
            {
                var parameters = new JObject
                {
                    ["strictBasics"] = strictBasicsParam,
                    ["hierarchyTopN"] = hierarchyTopNParam,
                    ["fdEventLimit"] = fdEventLimitParam,
                    ["leavePaused"] = leavePausedParam,
                    ["disableProfilerAfter"] = disableProfilerAfterParam
                };
                BeginCapture(parameters, null, null, onDone);
            }
            catch (Exception ex)
            {
                ClearPending(disposeRecorders: true);
                onDone(false, ex.Message);
                RaiseChanged();
            }
        }

        private static void BeginCapture(
            JToken parameters,
            string requestId,
            McpWebSocketClient client,
            Action<bool, string> onDone)
        {
            var sectionsArr = parameters?["sections"] as JArray;
            IEnumerable<string> sectionList = null;
            if (sectionsArr != null)
                sectionList = sectionsArr.Select(t => t?.ToString());

            sections = PerfCaptureOps.ParseSections(sectionList);

            hierarchyTopN = Math.Clamp(parameters?["hierarchyTopN"]?.Value<int?>() ?? 30, 1, 100);
            // 0 = 不返回 topEvents；>0 限制绘制顺序条数（最大 2000）
            {
                var rawFd = parameters?["fdEventLimit"]?.Value<int?>() ?? 0;
                if (rawFd < 0)
                    rawFd = 0;
                fdEventLimit = rawFd == 0 ? 0 : Math.Clamp(rawFd, 1, 2000);
            }
            leavePaused = parameters?["leavePaused"]?.Value<bool?>() ?? false;
            disableProfilerAfter = parameters?["disableProfilerAfter"]?.Value<bool?>() ?? false;
            strictBasics = parameters?["strictBasics"]?.Value<bool?>() ?? true;

            warnings = new List<string>();
            basicsSection = null;
            profileSection = null;
            frameDebuggerSection = null;
            overdrawSection = PerfCaptureOps.BuildOverdrawPlaceholder();
            basicsOkFlag = true;
            profileOkFlag = true;
            fdOkFlag = true;
            basicsAttempt = 0;
            frameDebuggerUsed = false;
            gameViewFocused = false;

            busy = true;
            pendingRequestId = requestId;
            pendingClient = client;
            localOnDone = onDone;
            autoEnteredPlay = false;
            waitingForPlay = false;

            // 未 Play 时自动进入（临时 DisableDomainReload，避免丢静态状态 / MCP 请求）
            if (!EditorApplication.isPlaying)
            {
                phase = Phase.WaitingForPlay;
                waitingForPlay = true;
                EnterPlayModeOptionsGuard.ApplyDisableDomainReload();
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                warnings.Add("未在 Play，自动进入 Play Mode。");
                autoEnteredPlay = true;
                EditorApplication.isPlaying = true;
                RaiseChanged();
                return;
            }

            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
                warnings.Add("Play 处于暂停，已自动恢复播放以便采数。");
            }

            StartPipelineAfterPlayReady();
        }

        private static void StartPipelineAfterPlayReady()
        {
            profilerWasEnabledBefore = UProfiler.enabled;
            if (!UProfiler.enabled)
            {
                var areas = new Dictionary<string, bool>
                {
                    { "CPU", true },
                    { "GPU", true },
                    { "Rendering", true }
                };
                var enable = ProfilerSessionOps.Enable(null, false, areas);
                if (!enable.Ok)
                    warnings.Add("Profiler enable 失败：" + enable.Error);
            }

            PerfCaptureOps.FocusGameView(out var focusDetail);
            gameViewFocused = true;
            if (!string.IsNullOrEmpty(focusDetail))
                warnings.Add(focusDetail);

            phase = Phase.PrepWait;
            // 刚进 Play 后前几帧 Render 常全 0（实测：第 1～2 次 basics 为 0，第 3 次才有 Batches）
            waitFrames = autoEnteredPlay ? 30 : 8;
            QueuePlayerLoop();
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            RaiseChanged();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!busy || !waitingForPlay)
                return;

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                waitingForPlay = false;
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EnterPlayModeOptionsGuard.Restore();
                try
                {
                    StartPipelineAfterPlayReady();
                }
                catch (Exception ex)
                {
                    FailPending("进入 Play 后启动采帧失败：" + ex.Message);
                }

                return;
            }

            if (state == PlayModeStateChange.ExitingEditMode)
                return;

            // 用户取消进 Play / 进 Play 失败
            if (state == PlayModeStateChange.EnteredEditMode && waitingForPlay)
            {
                waitingForPlay = false;
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EnterPlayModeOptionsGuard.Restore();
                FailPending("自动进入 Play Mode 未成功（仍停留在 Edit Mode）。");
            }
        }

        private static void OnUpdate()
        {
            if (!busy || phase == Phase.None)
            {
                EditorApplication.update -= OnUpdate;
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
                        // 等 playModeStateChanged
                        break;
                    case Phase.PrepWait:
                        StartOrRetryCounters();
                        break;
                    case Phase.CountersWait:
                        ReadCountersAndContinue();
                        break;
                    case Phase.AfterCounters:
                        BeginHierarchyFreshCapture();
                        break;
                    case Phase.HierarchyWait:
                        TryFinishHierarchyWait();
                        break;
                    case Phase.FdWait:
                        SnapshotFdAndFinish();
                        break;
                    case Phase.AfterFd:
                        CleanupAndReply();
                        break;
                }
            }
            catch (Exception ex)
            {
                FailPending(ex.Message);
            }
        }

        private static void StartOrRetryCounters()
        {
            if (!sections.Contains("basics"))
            {
                basicsSection = PerfCaptureOps.BuildSkippedSection("section not requested");
                basicsOkFlag = true;
                phase = Phase.AfterCounters;
                waitFrames = 0;
                return;
            }

            basicsAttempt++;
            if (!ProfilerCounterOps.TryBeginCapture(
                    "Render",
                    PerfCaptureOps.RenderCounterNames,
                    out pendingRecorders,
                    out pendingNames,
                    out var error))
            {
                basicsSection = new
                {
                    ok = false,
                    status = "failed",
                    error,
                    data = (object)null
                };
                basicsOkFlag = false;
                phase = Phase.AfterCounters;
                waitFrames = 0;
                return;
            }

            phase = Phase.CountersWait;
            waitFrames = 4 + basicsAttempt;
            QueuePlayerLoop();
        }

        private static void ReadCountersAndContinue()
        {
            var map = new Dictionary<string, long>(StringComparer.Ordinal);
            var diagnostics = new List<string>();
            try
            {
                for (var i = 0; i < pendingRecorders.Count; i++)
                {
                    var recorder = pendingRecorders[i];
                    var name = pendingNames[i];
                    long value = 0;
                    long current = 0;
                    long newest = 0;
                    var count = 0;
                    var valid = recorder.Valid;
                    if (valid)
                    {
                        current = recorder.CurrentValue;
                        count = recorder.Count;
                        // CurrentValue 对 per-frame Counter 更稳；样本环里 GetSample(0) 是最旧的
                        if (count > 0)
                            newest = recorder.GetSample(count - 1).Value;
                        value = current != 0 ? current : newest;
                    }

                    map[name] = value;
                    if (name == "Batches Count" || name == "Draw Calls Count" || name == "SetPass Calls Count")
                        diagnostics.Add($"{name}:valid={valid},count={count},current={current},newest={newest}");
                }
            }
            finally
            {
                ProfilerCounterOps.DisposeAll(pendingRecorders);
                pendingRecorders = null;
                pendingNames = null;
            }

            var section = PerfCaptureOps.BuildBasicsSection(map, strictBasics, out var ok);
            var allZero = map.TryGetValue("Batches Count", out var b) && b == 0
                          && map.TryGetValue("Draw Calls Count", out var d) && d == 0
                          && map.TryGetValue("SetPass Calls Count", out var s) && s == 0;

            if (strictBasics && allZero && basicsAttempt < 5)
            {
                warnings.Add($"basics 第 {basicsAttempt} 次为 0，等帧后重试（不抢焦点）。[{string.Join("; ", diagnostics)}]");
                PerfCaptureOps.FocusGameView(out _);
                phase = Phase.PrepWait;
                waitFrames = 8 + basicsAttempt * 4;
                QueuePlayerLoop();
                return;
            }

            if (allZero && diagnostics.Count > 0)
                warnings.Add("basics 诊断：" + string.Join("; ", diagnostics));

            basicsSection = section;
            basicsOkFlag = ok;
            phase = Phase.AfterCounters;
            waitFrames = 0;
        }

        /// <summary>
        /// 确保 ProfilerDriver 录制后等 lastFrameIndex 推进，再采 Hierarchy。
        /// 不清空帧：ClearAllFrames 后部分环境会一直 lastFrameIndex=-1。
        /// </summary>
        private static void BeginHierarchyFreshCapture()
        {
            if (!sections.Contains("profile"))
            {
                CaptureProfileThenMaybeFd(explicitFrameIndex: null);
                return;
            }

            var ensure = ProfilerSessionOps.EnsureDriverRecording();
            if (!ensure.Ok)
                warnings.Add("确保 ProfilerDriver 录制失败：" + ensure.Error);
            else
                warnings.Add("已确保 ProfilerDriver 录制，等待 lastFrame 推进后再采 Hierarchy。");

            hierarchyLastAtWaitStart = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
            hierarchyWaitAttempts = 0;
            phase = Phase.HierarchyWait;
            waitFrames = 10;
            QueuePlayerLoop();
        }

        private static void TryFinishHierarchyWait()
        {
            hierarchyWaitAttempts++;
            var first = UnityEditorInternal.ProfilerDriver.firstFrameIndex;
            var last = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
            var hasFrames = first >= 0 && last >= first;
            var advanced = hasFrames &&
                           (hierarchyLastAtWaitStart < 0
                               ? last >= 0
                               : last > hierarchyLastAtWaitStart);

            if (hasFrames && advanced)
            {
                CaptureProfileThenMaybeFd(explicitFrameIndex: last);
                return;
            }

            // Play 中再多等几轮，给 Driver 录上新帧
            if (hierarchyWaitAttempts < 8)
            {
                waitFrames = 8;
                QueuePlayerLoop();
                return;
            }

            warnings.Add(
                hierarchyLastAtWaitStart >= 0 && last == hierarchyLastAtWaitStart
                    ? $"等待 Hierarchy 帧推进超时（仍为 {last}），将尽力用当前 lastFrameIndex。"
                    : "等待 Hierarchy 新帧超时，将尽力用当前 lastFrameIndex。");
            CaptureProfileThenMaybeFd(explicitFrameIndex: hasFrames ? last : (int?)null);
        }

        private static void CaptureProfileThenMaybeFd(int? explicitFrameIndex)
        {
            if (sections.Contains("profile"))
            {
                object timingData = null;
                string timingError = null;
                var timingResult = ProfilerFrameTimingOps.Get();
                if (timingResult.Ok)
                    timingData = timingResult.Payload;
                else
                    timingError = timingResult.Error;

                object hierarchyItems = null;
                string hierarchyError = null;
                int? hierarchyFrame = null;

                var frameToUse = explicitFrameIndex;
                if (!frameToUse.HasValue)
                {
                    var last = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
                    var first = UnityEditorInternal.ProfilerDriver.firstFrameIndex;
                    if (first >= 0 && last >= first)
                        frameToUse = last;
                }

                if (frameToUse.HasValue)
                {
                    var hier = ProfilerHierarchyOps.GetTop(
                        frameToUse.Value,
                        0,
                        hierarchyTopN,
                        "selfMs",
                        null,
                        null,
                        null,
                        excludeEditorLoop: true,
                        preferPlayerLoop: true);
                    if (hier.Ok && hier.Payload != null)
                    {
                        hierarchyItems = ExtractHierarchyItems(hier.Payload);
                        hierarchyFrame = ExtractHierarchyFrameIndex(hier.Payload) ?? frameToUse;
                        if (hierarchyItems == null)
                            hierarchyError = "Hierarchy 返回格式异常。";
                    }
                    else
                    {
                        hierarchyError = hier.Error;
                    }
                }
                else
                {
                    hierarchyError = "Profiler 无可用 lastFrameIndex，无法采 Hierarchy。";
                }

                profileSection = PerfCaptureOps.BuildProfileSection(
                    timingData,
                    timingError,
                    hierarchyItems,
                    hierarchyError,
                    hierarchyFrame,
                    out profileOkFlag);
            }
            else
            {
                profileSection = PerfCaptureOps.BuildSkippedSection("section not requested");
                profileOkFlag = true;
            }

            if (sections.Contains("frameDebugger"))
            {
                var begin = FrameDebuggerOps.BeginEnable();
                if (!begin.Ok)
                {
                    frameDebuggerSection = new
                    {
                        ok = false,
                        status = "failed",
                        error = begin.Error,
                        data = (object)null
                    };
                    fdOkFlag = false;
                    phase = Phase.AfterFd;
                    waitFrames = 0;
                    return;
                }

                frameDebuggerUsed = true;
                phase = Phase.FdWait;
                waitFrames = 5;
                return;
            }

            frameDebuggerSection = PerfCaptureOps.BuildSkippedSection("section not requested");
            fdOkFlag = true;
            phase = Phase.AfterFd;
            waitFrames = 0;
        }

        private static void SnapshotFdAndFinish()
        {
            var snap = FrameDebuggerOps.SnapshotAfterEnable();
            frameDebuggerSection = PerfCaptureOps.BuildFrameDebuggerSection(fdEventLimit, out fdOkFlag);
            if (!fdOkFlag)
            {
                warnings.Add("Frame Debugger 摘要失败或事件为 0。");
                if (snap.Ok && snap.Payload != null)
                    warnings.Add("SnapshotAfterEnable 已执行。");
            }

            phase = Phase.AfterFd;
            waitFrames = 0;
        }

        private static void CleanupAndReply()
        {
            if (frameDebuggerUsed && !leavePaused)
            {
                var dis = FrameDebuggerOps.Disable();
                if (!dis.Ok)
                    warnings.Add("关闭 Frame Debugger 失败：" + dis.Error);
            }
            else if (frameDebuggerUsed && leavePaused)
            {
                warnings.Add("leavePaused=true：保持 Frame Debugger / 暂停状态，请稍后 unity_frame_debugger_disable。");
            }

            if (disableProfilerAfter && UProfiler.enabled)
                ProfilerSessionOps.Disable();

            if (overdrawSection == null)
                overdrawSection = PerfCaptureOps.BuildOverdrawPlaceholder();

            if (!sections.Contains("basics") && basicsSection == null)
                basicsSection = PerfCaptureOps.BuildSkippedSection("section not requested");
            if (!sections.Contains("profile") && profileSection == null)
                profileSection = PerfCaptureOps.BuildSkippedSection("section not requested");
            if (!sections.Contains("frameDebugger") && frameDebuggerSection == null)
                frameDebuggerSection = PerfCaptureOps.BuildSkippedSection("section not requested");

            var sectionList = sections.ToList();
            if (!sectionList.Contains("overdraw"))
                sectionList.Add("overdraw");

            var context = PerfCaptureOps.BuildContext(
                sectionList,
                gameViewFocused,
                profilerWasEnabledBefore || UProfiler.enabled,
                frameDebuggerUsed);

            var overallOk = true;
            if (sections.Contains("basics") && !basicsOkFlag)
                overallOk = false;
            if (sections.Contains("frameDebugger") && !fdOkFlag)
                warnings.Add("frameDebugger 块未成功。");
            if (sections.Contains("profile") && !profileOkFlag)
                warnings.Add("profile 块部分或全部 unavailable。");

            var payload = PerfCaptureOps.BuildRoot(
                overallOk,
                warnings,
                context,
                basicsSection,
                profileSection,
                frameDebuggerSection,
                overdrawSection);

            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            var client = pendingClient;
            var requestId = pendingRequestId;
            var cb = localOnDone;
            ClearPending(disposeRecorders: false);

            if (cb != null)
                cb(overallOk, json);
            else if (client != null && requestId != null)
                _ = client.SendResponseAsync(requestId, payload);

            RaiseChanged();
        }

        private static void FailPending(string error)
        {
            var client = pendingClient;
            var requestId = pendingRequestId;
            var cb = localOnDone;
            ClearPending(disposeRecorders: true);
            if (cb != null)
                cb(false, error);
            else if (client != null && requestId != null)
                _ = client.SendErrorAsync(requestId, -32000, error);
            RaiseChanged();
        }

        private static object ExtractHierarchyItems(object payload)
        {
            if (payload == null)
                return null;
            var type = payload.GetType();
            var prop = type.GetProperty("items") ?? type.GetProperty("Items");
            return prop?.GetValue(payload);
        }

        private static int? ExtractHierarchyFrameIndex(object payload)
        {
            if (payload == null)
                return null;
            var type = payload.GetType();
            var prop = type.GetProperty("frameIndex") ?? type.GetProperty("FrameIndex");
            var val = prop?.GetValue(payload);
            if (val is int i)
                return i;
            return null;
        }

        private static void QueuePlayerLoop()
        {
            try
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch
            {
                // ignore
            }
        }

        private static void ClearPending(bool disposeRecorders)
        {
            EditorApplication.update -= OnUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EnterPlayModeOptionsGuard.Restore();
            waitingForPlay = false;
            autoEnteredPlay = false;
            if (disposeRecorders)
                ProfilerCounterOps.DisposeAll(pendingRecorders);
            pendingRecorders = null;
            pendingNames = null;
            busy = false;
            phase = Phase.None;
            pendingRequestId = null;
            pendingClient = null;
            localOnDone = null;
            waitFrames = 0;
            sections = null;
            warnings = null;
        }

        private static void RaiseChanged()
        {
            try
            {
                Changed?.Invoke();
            }
            catch
            {
                // ignore UI listeners
            }
        }
    }
}
