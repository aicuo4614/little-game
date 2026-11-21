using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class FrameDebuggerEnableTool(ILogger<FrameDebuggerEnableTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_frame_debugger_enable")]
    [Description(
        "启用 Frame Debugger：打开分析窗口并捕获当前帧。" +
        "副作用：Play Mode 下会暂停游戏。需等数帧后 list 才有事件。" +
        "用完请调用 unity_frame_debugger_disable 恢复播放。")]
    public async Task<string> UnityFrameDebuggerEnableAsync()
    {
        logger.LogInformation("Frame debugger enable");
        try
        {
            // Enable waits several editor frames on Unity side.
            var result = await hub.SendRequestAsync("unity.frameDebuggerEnable", null, timeoutSeconds: 60);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"启用 Frame Debugger 失败：{ex.Message}";
        }
    }
}
