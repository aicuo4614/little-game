using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerStatusTool(ILogger<ProfilerStatusTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_status")]
    [Description("查询 Profiler 是否启用、recording 路径、allocation callstacks、帧范围与各 ProfilerArea 开关。")]
    public async Task<string> UnityProfilerStatusAsync()
    {
        logger.LogInformation("Profiler status");
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerStatus", null);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"查询 Profiler 状态失败：{ex.Message}";
        }
    }
}
