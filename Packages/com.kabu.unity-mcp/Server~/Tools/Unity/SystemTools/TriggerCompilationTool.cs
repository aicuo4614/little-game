using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class TriggerCompilationTool(ILogger<TriggerCompilationTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_trigger_script_compilation")]
    [Description("请求 Unity 重新编译脚本。随后可能触发 Domain Reload；WebSocket 会短暂断开，若此前已连接会自动重连。需要 Unity 已连接。")]
    public async Task<string> UnityTriggerScriptCompilationAsync()
    {
        logger.LogInformation("Trigger script compilation");
        try
        {
            var result = await hub.SendRequestAsync("unity.triggerCompilation", null);
            if (result?.TryGetProperty("message", out var msg) == true)
                return msg.GetString() ?? result.Value.ToString();
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"请求编译失败：{ex.Message}";
        }
    }
}
