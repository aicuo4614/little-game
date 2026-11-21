using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class GetPlayModeStateTool(ILogger<GetPlayModeStateTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_play_mode_state")]
    [Description("查询 Play Mode 状态：Stopped / Playing / Paused。")]
    public async Task<string> UnityGetPlayModeStateAsync()
    {
        logger.LogInformation("Get play mode state");
        try
        {
            var result = await hub.SendRequestAsync("unity.getPlayModeState", null);
            if (result?.TryGetProperty("message", out var msg) == true)
                return msg.GetString() ?? result.Value.ToString();
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex) { return $"查询失败：{ex.Message}"; }
    }
}
