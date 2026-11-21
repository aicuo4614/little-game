using System;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    internal static class PerfEvidenceHandlers
    {
        public static void GetOverdraw(string requestId, JToken parameters, McpWebSocketClient client)
        {
            // 异步 Play A/B；同步路径已废弃
            OverdrawAbHandlers.Capture(requestId, parameters, client);
        }

        public static void GetOcclusionInfo(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, OcclusionOps.GetInfo()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void GetPostProcessStack(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var max = parameters?["maxItems"]?.Value<int?>() ?? 80;
                Reply(requestId, client, PostProcessOps.GetStack(max));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void GetRenderCensus(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, RenderCensusOps.GetCensus()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
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
