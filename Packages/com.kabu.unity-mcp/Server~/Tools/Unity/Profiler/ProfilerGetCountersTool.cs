using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerGetCountersTool(ILogger<ProfilerGetCountersTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_get_counters")]
    [Description(
        "读取指定 category 的 ProfilerRecorder 计数器（等 1～2 编辑器帧后回包）。" +
        "counters 可省略：默认取该 category 前 40 个；一次最多 80 个。" +
        "返回 name/value/valid/unit。建议先 enable；Play Mode 数据更有意义。")]
    public async Task<string> UnityProfilerGetCountersAsync(
        [Description("类别名，如 Memory / Render / Scripts")] string category,
        [Description("可选计数器名列表；省略则取前 40 个")] string[]? counters = null)
    {
        logger.LogInformation("Profiler get counters category={Category}", category);
        try
        {
            var result = await hub.SendRequestAsync(
                "unity.profilerGetCounters",
                new { category, counters },
                timeoutSeconds: 60);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"读取计数器失败：{ex.Message}";
        }
    }
}
