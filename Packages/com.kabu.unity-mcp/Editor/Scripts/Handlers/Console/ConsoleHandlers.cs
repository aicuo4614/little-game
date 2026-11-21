using System;
using System.Linq;
using KabuUnityMcp.Editor.Services;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;

namespace KabuUnityMcp.Editor.Handlers.Console
{
    internal static class ConsoleHandlers
    {
        public static void GetLogs(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var maxEntries = parameters?["maxEntries"]?.Value<int?>() ?? 100;
                var logType = parameters?["logType"]?.ToString();
                var includeStackTrace = parameters?["includeStackTrace"]?.Value<bool>() ?? false;
                var lastMinutes = parameters?["lastMinutes"]?.Value<int?>() ?? 0;

                var logs = McpLogCollector.Query(maxEntries, logType, includeStackTrace, lastMinutes);
                _ = client.SendResponseAsync(requestId, new
                {
                    count = logs.Count,
                    logs = logs.Select(e => new
                    {
                        type = e.Type.ToString(),
                        message = e.Message,
                        stackTrace = e.StackTrace,
                        utcTime = e.UtcTime.ToString("o")
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void ClearLogs(string requestId, McpWebSocketClient client)
        {
            try
            {
                McpLogCollector.Clear();
                _ = client.SendResponseAsync(requestId, new
                {
                    cleared = true,
                    message = "已清空 Console 日志（编辑器 Console + MCP 采集缓冲）。"
                });
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }
    }
}
