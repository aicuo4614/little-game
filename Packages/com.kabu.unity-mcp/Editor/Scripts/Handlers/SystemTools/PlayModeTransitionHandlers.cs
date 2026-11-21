using System;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Editor.Handlers.Profiler;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.SystemTools
{
    /// <summary>
    /// Play Mode 切换：等 EnteredPlayMode / EnteredEditMode，再等 Player 帧推进。
    /// 「有没有东西可画」不是 Play 成功条件（空场景 Batches 合法为 0）；那是采帧 strictBasics 的事。
    /// </summary>
    internal static class PlayModeTransitionHandlers
    {
        private const double DefaultTimeoutSeconds = 45.0;
        /// <summary>EnteredPlayMode 后再等多少个 Time.frameCount 增量，视为 PlayerLoop 已在跑。</summary>
        private const int DefaultSettleFrames = 15;

        private static bool busy;
        private static string pendingRequestId;
        private static McpWebSocketClient pendingClient;
        private static bool waitingEnter;
        private static bool waitingSettle;
        private static bool waitingStep;
        private static bool disableDomainReload;
        private static double deadline;
        private static bool updateHooked;
        private static int settleTargetFrames;
        private static int frameCountAtSettleStart;
        private static int editorUpdatesSinceSettle;
        private static int settleFocusCountdown;
        private static int frameCountBeforeStep;
        private static bool wasPausedBeforeStep;

        public static void EnterPlayMode(string requestId, JToken parameters, McpWebSocketClient client)
        {
            if (busy)
            {
                _ = client.SendErrorAsync(requestId, -32000, "Play Mode 切换进行中，请稍后重试。");
                return;
            }

            try
            {
                if (EditorApplication.isPlaying)
                {
                    Reply(requestId, client, HandlerResult.Fail("已处于 Play Mode。"));
                    return;
                }

                var disableReload = ReadDisableDomainReload(parameters);
                settleTargetFrames = ReadSettleFrames(parameters);
                if (disableReload)
                    EnterPlayModeOptionsGuard.ApplyDisableDomainReload();

                busy = true;
                waitingEnter = true;
                waitingSettle = false;
                waitingStep = false;
                disableDomainReload = disableReload;
                pendingRequestId = requestId;
                pendingClient = client;
                deadline = EditorApplication.timeSinceStartup + DefaultTimeoutSeconds;

                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                EnsureUpdateHook();
                EditorApplication.isPlaying = true;
            }
            catch (Exception ex)
            {
                ClearPending(restoreGuard: true);
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void ExitPlayMode(string requestId, McpWebSocketClient client)
        {
            if (busy)
            {
                _ = client.SendErrorAsync(requestId, -32000, "Play Mode 切换进行中，请稍后重试。");
                return;
            }

            try
            {
                if (!EditorApplication.isPlaying)
                {
                    Reply(requestId, client, HandlerResult.Success(new
                    {
                        exited = false,
                        state = "Stopped",
                        message = "已处于停止状态，无需退出。"
                    }));
                    return;
                }

                busy = true;
                waitingEnter = false;
                waitingSettle = false;
                waitingStep = false;
                pendingRequestId = requestId;
                pendingClient = client;
                deadline = EditorApplication.timeSinceStartup + DefaultTimeoutSeconds;

                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                EnsureUpdateHook();
                EditorApplication.isPlaying = false;
            }
            catch (Exception ex)
            {
                ClearPending(restoreGuard: false);
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        /// <summary>
        /// 等价工具栏「下一帧」：<see cref="EditorApplication.Step"/>，等 Time.frameCount 推进后返回。
        /// 须已在 Play Mode；步进后通常处于 Paused。
        /// </summary>
        public static void StepPlayModeFrame(string requestId, McpWebSocketClient client)
        {
            if (busy)
            {
                _ = client.SendErrorAsync(requestId, -32000, "Play Mode 切换进行中，请稍后重试。");
                return;
            }

            try
            {
                if (!EditorApplication.isPlaying)
                {
                    Reply(requestId, client, HandlerResult.Fail("须先进入 Play Mode，才能单步下一帧。"));
                    return;
                }

                busy = true;
                waitingEnter = false;
                waitingSettle = false;
                waitingStep = true;
                pendingRequestId = requestId;
                pendingClient = client;
                deadline = EditorApplication.timeSinceStartup + 10.0;
                frameCountBeforeStep = Time.frameCount;
                wasPausedBeforeStep = EditorApplication.isPaused;
                editorUpdatesSinceSettle = 0;

                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                EnsureUpdateHook();
                EditorApplication.Step();
                QueuePlayerLoop();
            }
            catch (Exception ex)
            {
                ClearPending(restoreGuard: false);
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        private static bool ReadDisableDomainReload(JToken parameters)
        {
            if (parameters == null || parameters.Type != JTokenType.Object)
                return true;
            return parameters["disableDomainReload"]?.Value<bool?>() ?? true;
        }

        private static int ReadSettleFrames(JToken parameters)
        {
            if (parameters == null || parameters.Type != JTokenType.Object)
                return DefaultSettleFrames;
            var n = parameters["settleFrames"]?.Value<int?>() ?? DefaultSettleFrames;
            return Math.Clamp(n, 0, 120);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!busy)
                return;

            if (waitingEnter && state == PlayModeStateChange.EnteredPlayMode)
            {
                waitingEnter = false;
                if (settleTargetFrames <= 0)
                {
                    FinishEnter(ok: true, null);
                    return;
                }

                // PlayerLoop：聚焦 Game；强制后台也可跑；用 Editor update 计数保证 settle
                waitingSettle = true;
                frameCountAtSettleStart = Time.frameCount;
                editorUpdatesSinceSettle = 0;
                settleFocusCountdown = 2;
                try { Application.runInBackground = true; } catch { /* ignore */ }
                PerfCaptureOps.FocusGameView(out _);
                QueuePlayerLoop();
                return;
            }

            if (waitingEnter && state == PlayModeStateChange.EnteredEditMode)
            {
                FinishEnter(ok: false, "进入 Play Mode 未成功（仍停留在 Edit Mode）。");
                return;
            }

            if (waitingSettle && state == PlayModeStateChange.EnteredEditMode)
            {
                FinishEnter(ok: false, "等待 Player 帧推进时已退出 Play Mode。");
                return;
            }

            if (waitingStep && state == PlayModeStateChange.EnteredEditMode)
            {
                FinishStep(ok: false, "单步过程中已退出 Play Mode。");
                return;
            }

            if (!waitingEnter && !waitingSettle && !waitingStep && state == PlayModeStateChange.EnteredEditMode)
                FinishExit(ok: true, null);
        }

        private static void OnUpdate()
        {
            if (!busy)
            {
                RemoveUpdateHook();
                return;
            }

            if (waitingStep)
            {
                TickStep();
                return;
            }

            if (waitingSettle)
            {
                TickSettle();
                return;
            }

            if (EditorApplication.timeSinceStartup < deadline)
                return;

            if (waitingEnter)
                FinishEnter(ok: false, $"等待进入 Play Mode 超时（{DefaultTimeoutSeconds}s）。");
            else
                FinishExit(ok: false, $"等待退出 Play Mode 超时（{DefaultTimeoutSeconds}s）。");
        }

        private static void TickStep()
        {
            editorUpdatesSinceSettle++;
            QueuePlayerLoop();

            if (EditorApplication.timeSinceStartup >= deadline)
            {
                FinishStep(
                    ok: false,
                    $"等待单步帧超时（editorUpdates={editorUpdatesSinceSettle}，" +
                    $"Time.frameCount 仍为 {Time.frameCount}，步进前 {frameCountBeforeStep}）。");
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                FinishStep(ok: false, "单步过程中已退出 Play Mode。");
                return;
            }
            
            // Step 成功后 frameCount 会 +1，且通常停在 Paused
            if (Time.frameCount > frameCountBeforeStep)
            {
                FinishStep(ok: true, null);
            }
        }

        private static void FinishStep(bool ok, string error)
        {
            var client = pendingClient;
            var requestId = pendingRequestId;
            var before = frameCountBeforeStep;
            var wasPaused = wasPausedBeforeStep;
            var advanced = Math.Max(0, Time.frameCount - before);
            ClearPending(restoreGuard: false);

            if (client == null || requestId == null)
                return;

            if (!ok)
            {
                Reply(requestId, client, HandlerResult.Fail(error ?? "单步下一帧失败。"));
                return;
            }

            var state = EditorApplication.isPaused ? "Paused" : "Playing";
            Reply(requestId, client, HandlerResult.Success(new
            {
                stepped = true,
                state,
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                wasPausedBeforeStep = wasPaused,
                frameCountBefore = before,
                frameCount = Time.frameCount,
                framesAdvanced = advanced,
                message = $"已单步下一帧（Time.frameCount {before}→{Time.frameCount}，当前 {state}）。"
            }));
        }

        private static void TickSettle()
        {
            if (EditorApplication.timeSinceStartup >= deadline)
            {
                FinishEnter(
                    ok: false,
                    $"已进入 Play，但等待 settle 超时（{DefaultTimeoutSeconds}s，editorUpdates={editorUpdatesSinceSettle}/{settleTargetFrames}，" +
                    $"Time.frameCount +{Math.Max(0, Time.frameCount - frameCountAtSettleStart)}）。");
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                FinishEnter(ok: false, "等待 settle 时不在 Play Mode。");
                return;
            }

            if (EditorApplication.isPaused)
                EditorApplication.isPaused = false;

            if (settleFocusCountdown > 0)
            {
                settleFocusCountdown--;
                if (settleFocusCountdown == 0)
                    PerfCaptureOps.FocusGameView(out _);
            }

            QueuePlayerLoop();
            editorUpdatesSinceSettle++;

            // 以 Editor update 为准（必定推进）；Time.frameCount 仅作附加信息
            if (editorUpdatesSinceSettle >= settleTargetFrames)
                FinishEnter(ok: true, null);
        }

        private static void FinishEnter(bool ok, string error)
        {
            var client = pendingClient;
            var requestId = pendingRequestId;
            var usedDisable = disableDomainReload;
            var settled = settleTargetFrames;
            var editorUpdates = editorUpdatesSinceSettle;
            var advanced = Math.Max(0, Time.frameCount - frameCountAtSettleStart);
            ClearPending(restoreGuard: true);

            if (client == null || requestId == null)
                return;

            if (!ok)
            {
                Reply(requestId, client, HandlerResult.Fail(error ?? "进入 Play Mode 失败。"));
                return;
            }

            Reply(requestId, client, HandlerResult.Success(new
            {
                entered = true,
                state = EditorApplication.isPaused ? "Paused" : "Playing",
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                settleFrames = settled,
                editorUpdates,
                framesAdvanced = advanced,
                frameCount = Time.frameCount,
                disableDomainReload = usedDisable,
                message =
                    $"已进入 Play Mode（Playing，settle {editorUpdates} editor updates，Time.frameCount +{advanced}）。" +
                    " 不保证场景有可画内容；采帧看 basics/strictBasics。"
            }));
        }

        private static void FinishExit(bool ok, string error)
        {
            var client = pendingClient;
            var requestId = pendingRequestId;
            ClearPending(restoreGuard: false);

            if (client == null || requestId == null)
                return;

            if (!ok)
            {
                Reply(requestId, client, HandlerResult.Fail(error ?? "退出 Play Mode 失败。"));
                return;
            }

            Reply(requestId, client, HandlerResult.Success(new
            {
                exited = true,
                state = "Stopped",
                isPlaying = EditorApplication.isPlaying,
                message = "已退出 Play Mode。"
            }));
        }

        private static void ClearPending(bool restoreGuard)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            RemoveUpdateHook();
            if (restoreGuard)
                EnterPlayModeOptionsGuard.Restore();

            busy = false;
            waitingEnter = false;
            waitingSettle = false;
            waitingStep = false;
            pendingRequestId = null;
            pendingClient = null;
            disableDomainReload = true;
            settleTargetFrames = DefaultSettleFrames;
            frameCountAtSettleStart = 0;
            editorUpdatesSinceSettle = 0;
            settleFocusCountdown = 0;
            frameCountBeforeStep = 0;
            wasPausedBeforeStep = false;
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

        private static void QueuePlayerLoop()
        {
            try { EditorApplication.QueuePlayerLoopUpdate(); }
            catch { /* ignore */ }
        }

        private static void Reply(string requestId, McpWebSocketClient client, HandlerResult result)
        {
            if (result.Ok)
                _ = client.SendResponseAsync(requestId, result.Payload);
            else
                _ = client.SendErrorAsync(requestId, result.ErrorCode, result.Error);
        }
    }
}
