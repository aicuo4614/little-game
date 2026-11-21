using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerListCountersTool(ILogger<ProfilerListCountersTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_list_counters")]
    [Description(
        "分页列出某 Profiler category 下的计数器名称（Render/Scripts/Memory/Physics/Animation 等）。" +
        "先 list 再 get_counters。支持 nameContains 过滤。")]
    public async Task<string> UnityProfilerListCountersAsync(
        [Description("类别名，如 Memory / Render / Scripts")] string category,
        [Description("起始偏移，默认 0")] int offset = 0,
        [Description("返回条数，默认 50，最大 200")] int limit = 50,
        [Description("按名称子串过滤")] string? nameContains = null)
    {
        logger.LogInformation("Profiler list counters category={Category}", category);
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerListCounters", new
            {
                category,
                offset,
                limit,
                nameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains.Trim()
            });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"列出计数器失败：{ex.Message}";
        }
    }
}
