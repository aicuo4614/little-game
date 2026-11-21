using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerDisableTool(ILogger<ProfilerDisableTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_disable")]
    [Description("关闭 Unity Profiler，停止 binary log 与 allocation callstacks。")]
    public async Task<string> UnityProfilerDisableAsync()
    {
        logger.LogInformation("Profiler disable");
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerDisable", null);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"关闭 Profiler 失败：{ex.Message}";
        }
    }
}
