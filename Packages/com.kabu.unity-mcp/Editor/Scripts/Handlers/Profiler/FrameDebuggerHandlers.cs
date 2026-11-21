using System;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    internal static class FrameDebuggerHandlers
    {
        private static bool busy;
        private static string pendingRequestId;
        private static McpWebSocketClient pendingClient;
        private static int pendingIndex;
        private static int waitFrames;
        private static int pollCount;
        private static PendingKind pendingKind;

        private enum PendingKind
        {
            None,
            Enable,
            GetEvent
        }

        public static void Enable(string requestId, McpWebSocketClient client)
        {
            if (busy)
            {
                _ = client.SendErrorAsync(requestId, -32000, "Frame Debugger 操作进行中，请稍后重试。");
                return;
            }

            try
            {
                var begin = FrameDebuggerOps.BeginEnable();
                if (!begin.Ok)
                {
                    _ = client.SendErrorAsync(requestId, begin.ErrorCode, begin.Error);
                    return;
                }

                busy = true;
                pendingKind = PendingKind.Enable;
                pendingRequestId = requestId;
                pendingClient = client;
                waitFrames = 4;
                pollCount = 0;
                EditorApplication.update += OnUpdate;
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Disable(string requestId, McpWebSocketClient client)
        {
            try
            {
                CancelPending("被 disable 取消。");
                Reply(requestId, client, FrameDebuggerOps.Disable());
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void List(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var offset = parameters?["offset"]?.Value<int?>() ?? 0;
                var limit = parameters?["limit"]?.Value<int?>() ?? 50;
                var eventType = parameters?["eventType"]?.ToString();
                var nameContains = parameters?["nameContains"]?.ToString();
                Reply(requestId, client, FrameDebuggerOps.List(offset, limit, eventType, nameContains));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void GetEvent(string requestId, JToken parameters, McpWebSocketClient client)
        {
            if (busy)
            {
                _ = client.SendErrorAsync(requestId, -32000, "Frame Debugger 操作进行中，请稍后重试。");
                return;
            }

            try
            {
                var index = parameters?["index"]?.Value<int?>();
                if (index == null)
                {
                    _ = client.SendErrorAsync(requestId, -32602, "请提供 index（来自 list 的序号）。");
                    return;
                }

                if (!FrameDebuggerOps.TrySeek(index.Value, out var error))
                {
                    _ = client.SendErrorAsync(requestId, -32000, error);
                    return;
                }

                busy = true;
                pendingKind = PendingKind.GetEvent;
                pendingRequestId = requestId;
                pendingClient = client;
                pendingIndex = index.Value;
                waitFrames = 1;
                pollCount = 0;
                EditorApplication.update += OnUpdate;
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        private static void OnUpdate()
        {
            if (!busy || pendingKind == PendingKind.None)
            {
                EditorApplication.update -= OnUpdate;
                return;
            }

            if (waitFrames > 0)
            {
                waitFrames--;
                return;
            }

            var client = pendingClient;
            var requestId = pendingRequestId;
            var kind = pendingKind;

            try
            {
                if (kind == PendingKind.Enable)
                {
                    Finish(FrameDebuggerOps.SnapshotAfterEnable());
                    return;
                }

                if (kind == PendingKind.GetEvent)
                {
                    var data = FrameDebuggerApi.TryGetEventData(pendingIndex);
                    if (data == null && pollCount < 8)
                    {
                        pollCount++;
                        waitFrames = 1;
                        FrameDebuggerApi.SeekToEvent(pendingIndex);
                        return;
                    }

                    Finish(FrameDebuggerOps.BuildEventDetail(pendingIndex));
                }
            }
            catch (Exception ex)
            {
                ClearPending();
                if (client != null && requestId != null)
                {
                    _ = client.SendErrorAsync(requestId, -32000, ex.Message);
                }
            }
        }

        private static void Finish(HandlerResult result)
        {
            var client = pendingClient;
            var requestId = pendingRequestId;
            ClearPending();
            if (client == null || requestId == null)
            {
                return;
            }

            Reply(requestId, client, result);
        }

        private static void CancelPending(string reason)
        {
            if (!busy)
            {
                return;
            }

            var client = pendingClient;
            var requestId = pendingRequestId;
            ClearPending();
            if (client != null && requestId != null)
            {
                _ = client.SendErrorAsync(requestId, -32000, reason);
            }
        }

        private static void ClearPending()
        {
            EditorApplication.update -= OnUpdate;
            busy = false;
            pendingKind = PendingKind.None;
            pendingRequestId = null;
            pendingClient = null;
            pendingIndex = 0;
            waitFrames = 0;
            pollCount = 0;
        }

        private static void Reply(string requestId, McpWebSocketClient client, HandlerResult result)
        {
            if (result.Ok)
            {
                _ = client.SendResponseAsync(requestId, result.Payload);
            }
            else
            {
                _ = client.SendErrorAsync(requestId, result.ErrorCode, result.Error);
            }
        }
    }
}
