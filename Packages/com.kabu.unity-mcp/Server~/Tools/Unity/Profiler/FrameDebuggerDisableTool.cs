using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class FrameDebuggerDisableTool(ILogger<FrameDebuggerDisableTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_frame_debugger_disable")]
    [Description("关闭 Frame Debugger。若此前因 enable 暂停了 Play Mode，会恢复播放。")]
    public async Task<string> UnityFrameDebuggerDisableAsync()
    {
        logger.LogInformation("Frame debugger disable");
        try
        {
            var result = await hub.SendRequestAsync("unity.frameDebuggerDisable", null);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"关闭 Frame Debugger 失败：{ex.Message}";
        }
    }
}
