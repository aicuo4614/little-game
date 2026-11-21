using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Console;

[McpServerToolType]
public class ClearConsoleLogsTool(ILogger<ClearConsoleLogsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_clear_console_logs")]
    [Description("清空 Unity Editor Console 与 MCP 日志缓冲。")]
    [return: Description("清空结果")]
    public async Task<string> UnityClearConsoleLogsAsync()
    {
        logger.LogInformation("Clear console logs");
        try
        {
            var result = await hub.SendRequestAsync("unity.clearConsoleLogs", null);
            if (result == null)
                return "已请求清空，但 Unity 未返回数据。";
            if (result.Value.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "已清空。";
            return result.Value.ToString();
        }
        catch (Exception ex)
        {
            return $"清空日志失败：{ex.Message}";
        }
    }
}
