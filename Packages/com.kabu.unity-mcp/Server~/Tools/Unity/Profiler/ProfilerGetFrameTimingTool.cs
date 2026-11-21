using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerGetFrameTimingTool(ILogger<ProfilerGetFrameTimingTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_get_frame_timing")]
    [Description(
        "读取 FrameTimingManager 最新帧时（CPU/GPU/主线程/渲染线程 ms）。" +
        "前置：Player Settings 开启 Frame Timing Stats（或 Development Build）；建议 Play Mode 跑若干帧后再取。")]
    public async Task<string> UnityProfilerGetFrameTimingAsync()
    {
        logger.LogInformation("Profiler get frame timing");
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerGetFrameTiming", null);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"读取帧时失败：{ex.Message}";
        }
    }
}
