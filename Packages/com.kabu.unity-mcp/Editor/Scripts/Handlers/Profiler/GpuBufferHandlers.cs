using System;
using KabuUnityMcp.Gpu;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.Rendering;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    internal static class GpuBufferHandlers
    {
        private static bool busy;
        private static string pendingRequestId;
        private static McpWebSocketClient pendingClient;
        private static AsyncGPUReadbackRequest pendingRequest;
        private static McpGpuBufferEntry pendingEntry;
        private static int pendingOffset;
        private static int pendingLimit;
        private static int pollCount;

        public static void List(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var offset = parameters?["offset"]?.Value<int?>() ?? 0;
                var limit = parameters?["limit"]?.Value<int?>() ?? GpuBufferOps.DefaultLimit;
                var nameContains = parameters?["nameContains"]?.ToString();
                var refreshHost = parameters?["refreshHost"]?.Value<bool?>() ?? true;
                Reply(requestId, client, GpuBufferOps.List(offset, limit, nameContains, refreshHost));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Read(string requestId, JToken parameters, McpWebSocketClient client)
        {
            if (busy)
            {
                _ = client.SendErrorAsync(requestId, -32000, "GPU buffer 读取进行中，请稍后重试。");
                return;
            }

            try
            {
                var bufferId = parameters?["bufferId"]?.ToString() ?? parameters?["id"]?.ToString();
                var offset = parameters?["offset"]?.Value<int?>() ?? 0;
                var limit = parameters?["limit"]?.Value<int?>() ?? GpuBufferOps.DefaultLimit;
                var source = parameters?["source"]?.ToString();
                if (string.IsNullOrWhiteSpace(source))
                {
                    source = "gpu";
                }

                source = source.Trim().ToLowerInvariant();
                if (source == "cpu")
                {
                    Reply(requestId, client, GpuBufferOps.ReadCpu(bufferId, offset, limit));
                    return;
                }

                if (source != "gpu")
                {
                    _ = client.SendErrorAsync(requestId, -32602, "source 须为 gpu 或 cpu。");
                    return;
                }

                GpuBufferOps.NormalizeSlice(ref offset, ref limit);
                if (!GpuBufferOps.TryBeginGpuRead(bufferId, offset, limit, out var request, out var entry, out var error))
                {
                    _ = client.SendErrorAsync(requestId, -32000, error);
                    return;
                }

                busy = true;
                pendingRequestId = requestId;
                pendingClient = client;
                pendingRequest = request;
                pendingEntry = entry;
                pendingOffset = offset;
                pendingLimit = limit;
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
            if (!busy)
            {
                EditorApplication.update -= OnUpdate;
                return;
            }

            var client = pendingClient;
            var requestId = pendingRequestId;

            try
            {
                if (!pendingRequest.done)
                {
                    pollCount++;
                    if (pollCount > 120)
                    {
                        Finish(HandlerResult.Fail("AsyncGPUReadback 超时（约 2s）。"));
                    }

                    return;
                }

                Finish(GpuBufferOps.CompleteGpuRead(pendingEntry, pendingRequest, pendingOffset, pendingLimit));
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

        private static void ClearPending()
        {
            EditorApplication.update -= OnUpdate;
            busy = false;
            pendingRequestId = null;
            pendingClient = null;
            pendingEntry = null;
            pendingOffset = 0;
            pendingLimit = 0;
            pollCount = 0;
            pendingRequest = default;
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
