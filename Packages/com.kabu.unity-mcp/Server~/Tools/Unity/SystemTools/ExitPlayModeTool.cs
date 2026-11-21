using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class ExitPlayModeTool(ILogger<ExitPlayModeTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_exit_play_mode")]
    [Description("退出 Play Mode，并等到真正回到 Stopped 后才返回（超时约 30s）。已停止时视为成功。")]
    public async Task<string> UnityExitPlayModeAsync()
    {
        logger.LogInformation("Exit play mode");
        try
        {
            var result = await hub.SendRequestAsync("unity.exitPlayMode", null, timeoutSeconds: 45);
            if (result?.TryGetProperty("message", out var msg) == true)
                return msg.GetString() ?? "已退出。";
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex) { return $"退出 Play Mode 失败：{ex.Message}"; }
    }
}
