using System;
using System.Collections.Generic;
using System.Linq;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEditor;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>Profiler MCP handlers：同步 Ops + 等帧/等回调的异步路径。</summary>
    internal static class ProfilerHandlers
    {
        private static bool busy;
        private static string pendingRequestId;
        private static McpWebSocketClient pendingClient;
        private static PendingKind pendingKind;
        private static int waitFrames;
        private static string pendingCategory;
        private static List<ProfilerRecorder> pendingRecorders;
        private static List<string> pendingNames;

        private enum PendingKind
        {
            None,
            GetCounters
        }

        public static void Enable(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var logFile = parameters?["logFile"]?.ToString();
                var enableCallstacks = parameters?["enableCallstacks"]?.Value<bool?>() ?? false;
                var areas = ParseAreas(parameters?["areas"]);
                Reply(requestId, client, ProfilerSessionOps.Enable(logFile, enableCallstacks, areas));
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
                CancelPending("被 profiler disable 取消。");
                Reply(requestId, client, ProfilerSessionOps.Disable());
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Status(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, ProfilerSessionOps.Status()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void SetAreas(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, ProfilerSessionOps.SetAreas(ParseAreas(parameters?["areas"])));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Clear(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, ProfilerSessionOps.Clear()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void GetFrameTiming(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, ProfilerFrameTimingOps.Get()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void ListCounters(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var category = parameters?["category"]?.ToString();
                var offset = parameters?["offset"]?.Value<int?>() ?? 0;
                var limit = parameters?["limit"]?.Value<int?>() ?? 50;
                var nameContains = parameters?["nameContains"]?.ToString();
                Reply(requestId, client, ProfilerCounterOps.List(category, offset, limit, nameContains));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void GetCounters(string requestId, JToken parameters, McpWebSocketClient client)
        {
            if (busy)
            {
                _ = client.SendErrorAsync(requestId, -32000, "Profiler 异步操作进行中，请稍后重试。");
                return;
            }

            try
            {
                var category = parameters?["category"]?.ToString();
                List<string> names = null;
                var countersToken = parameters?["counters"];
                if (countersToken is JArray arr)
                {
                    names = arr.Select(t => t?.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                }
                else if (countersToken != null && countersToken.Type == JTokenType.String)
                {
                    var single = countersToken.ToString();
                    if (!string.IsNullOrWhiteSpace(single))
                        names = new List<string> { single };
                }

                if (!ProfilerCounterOps.TryBeginCapture(category, names, out var recorders, out var resolvedNames, out var error))
                {
                    _ = client.SendErrorAsync(requestId, -32000, error);
                    return;
                }

                busy = true;
                pendingKind = PendingKind.GetCounters;
                pendingRequestId = requestId;
                pendingClient = client;
                pendingCategory = category?.Trim();
                pendingRecorders = recorders;
                pendingNames = resolvedNames;
                waitFrames = 2;
                try
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                catch
                {
                    // Edit Mode 下可能受限
                }

                EditorApplication.update += OnUpdate;
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void GetHierarchy(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var frameIndex = parameters?["frameIndex"]?.Value<int?>();
                var threadIndex = parameters?["threadIndex"]?.Value<int?>() ?? 0;
                var topN = parameters?["topN"]?.Value<int?>() ?? 20;
                var sortBy = parameters?["sortBy"]?.ToString();
                var nameContains = parameters?["nameContains"]?.ToString();
                var minMs = parameters?["minMs"]?.Value<double?>();
                var maxDepth = parameters?["maxDepth"]?.Value<int?>();
                Reply(
                    requestId,
                    client,
                    ProfilerHierarchyOps.GetTop(frameIndex, threadIndex, topN, sortBy, nameContains, minMs, maxDepth));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void GetHierarchyChildren(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var frameIndex = parameters?["frameIndex"]?.Value<int?>();
                var threadIndex = parameters?["threadIndex"]?.Value<int?>() ?? 0;
                var itemId = parameters?["itemId"]?.Value<int?>();
                if (itemId == null)
                {
                    _ = client.SendErrorAsync(requestId, -32602, "请提供 itemId（来自 get_hierarchy 的 id）。");
                    return;
                }

                var offset = parameters?["offset"]?.Value<int?>() ?? 0;
                var limit = parameters?["limit"]?.Value<int?>() ?? 50;
                var nameContains = parameters?["nameContains"]?.ToString();
                Reply(
                    requestId,
                    client,
                    ProfilerHierarchyOps.GetChildren(
                        frameIndex, threadIndex, itemId.Value, offset, limit, nameContains));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void GetObjectMemory(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var instanceId = parameters?["instanceId"]?.Value<int?>();
                var path = parameters?["path"]?.ToString() ?? parameters?["objectPath"]?.ToString();
                var name = parameters?["name"]?.ToString();
                Reply(requestId, client, ProfilerObjectMemoryOps.Get(instanceId, path, name));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void MemoryTakeSnapshot(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var snapshotPath = parameters?["snapshotPath"]?.ToString();
                var targetId = parameters?["expectedTargetId"]?.Value<int?>();
                var flags = parameters?["captureFlags"]?.Value<int?>() ?? 7;
                var timeout = parameters?["timeoutSeconds"]?.Value<int?>() ?? 180;
                if (!ProfilerMemorySnapshotOps.TryBeginTake(snapshotPath,
                    result => Reply(requestId, client, result), out var error, targetId, flags, timeout))
                    _ = client.SendErrorAsync(requestId, -32000, error);
            }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void MemoryTargets(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var targetId = parameters?["targetId"]?.Value<int?>();
                Reply(requestId, client, targetId.HasValue
                    ? ProfilerMemoryTargetOps.Select(targetId.Value) : ProfilerMemoryTargetOps.List());
            }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void MemoryListSnapshots(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var searchPath = parameters?["searchPath"]?.ToString();
                var offset = parameters?["offset"]?.Value<int?>() ?? 0;
                var limit = parameters?["limit"]?.Value<int?>() ?? 50;
                Reply(requestId, client, ProfilerMemorySnapshotOps.List(searchPath, offset, limit));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void MemoryCompareSnapshots(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var a = parameters?["snapshotA"]?.ToString();
                var b = parameters?["snapshotB"]?.ToString();
                Reply(requestId, client, ProfilerMemorySnapshotOps.Compare(a, b));
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

            if (pendingKind == PendingKind.GetCounters)
            {
                if (waitFrames > 0)
                {
                    waitFrames--;
                    try
                    {
                        EditorApplication.QueuePlayerLoopUpdate();
                    }
                    catch
                    {
                        // ignore
                    }

                    return;
                }

                try
                {
                    var result = ProfilerCounterOps.ReadAndDispose(
                        pendingCategory, pendingRecorders, pendingNames);
                    pendingRecorders = null;
                    pendingNames = null;
                    Finish(result);
                }
                catch (Exception ex)
                {
                    ProfilerCounterOps.DisposeAll(pendingRecorders);
                    pendingRecorders = null;
                    pendingNames = null;
                    var client = pendingClient;
                    var requestId = pendingRequestId;
                    ClearPending();
                    if (client != null && requestId != null)
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
                return;
            Reply(requestId, client, result);
        }

        private static void CancelPending(string reason)
        {
            if (!busy)
                return;

            var client = pendingClient;
            var requestId = pendingRequestId;
            if (pendingKind == PendingKind.GetCounters)
                ProfilerCounterOps.DisposeAll(pendingRecorders);

            ClearPending();
            if (client != null && requestId != null)
                _ = client.SendErrorAsync(requestId, -32000, reason);
        }

        private static void ClearPending()
        {
            EditorApplication.update -= OnUpdate;
            busy = false;
            pendingKind = PendingKind.None;
            pendingRequestId = null;
            pendingClient = null;
            waitFrames = 0;
            pendingCategory = null;
            pendingRecorders = null;
            pendingNames = null;
        }

        private static void Reply(string requestId, McpWebSocketClient client, HandlerResult result)
        {
            if (result.Ok)
                _ = client.SendResponseAsync(requestId, result.Payload);
            else
                _ = client.SendErrorAsync(requestId, result.ErrorCode, result.Error);
        }

        private static Dictionary<string, bool> ParseAreas(JToken token)
        {
            var obj = token as JObject;
            if (obj == null)
                return null;

            var areas = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in obj.Properties())
            {
                if (prop.Value.Type != JTokenType.Boolean)
                    throw new ArgumentException($"area '{prop.Name}' 的值必须是 bool，实际为 {prop.Value.Type}。");
                areas[prop.Name] = prop.Value.Value<bool>();
            }

            return areas;
        }
    }
}
