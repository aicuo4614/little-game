using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerClearTool(ILogger<ProfilerClearTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_clear")]
    [Description("清空 Profiler 已录制帧（ProfilerDriver.ClearAllFrames）。不影响 enabled 状态。")]
    public async Task<string> UnityProfilerClearAsync()
    {
        logger.LogInformation("Profiler clear");
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerClear", null);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"清空 Profiler 失败：{ex.Message}";
        }
    }
}
