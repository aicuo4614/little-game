using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerGetHierarchyTool(ILogger<ProfilerGetHierarchyTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_get_hierarchy")]
    [Description(
        "读取 CPU Usage Hierarchy 热点 Top N（默认最近一帧、主线程）。" +
        "返回 id/name/depth/totalMs/selfMs/gcAlloc/calls/hasChildren。" +
        "sortBy: selfMs|totalMs|gcAlloc。可 nameContains/minMs/maxDepth 过滤。" +
        "禁止整树 dump；子节点用 unity_profiler_get_hierarchy_children。" +
        "须先 unity_profiler_enable 并录制若干帧。")]
    public async Task<string> UnityProfilerGetHierarchyAsync(
        [Description("帧索引；省略则用 lastFrameIndex")] int? frameIndex = null,
        [Description("线程索引，默认 0（主线程）")] int threadIndex = 0,
        [Description("返回条数，默认 20，最大 100")] int topN = 20,
        [Description("排序：selfMs / totalMs / gcAlloc")] string sortBy = "selfMs",
        [Description("按名称子串过滤")] string? nameContains = null,
        [Description("最小 self/total ms 过滤")] double? minMs = null,
        [Description("最大遍历深度，默认 64")] int? maxDepth = null)
    {
        logger.LogInformation("Profiler get hierarchy topN={TopN}", topN);
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerGetHierarchy", new
            {
                frameIndex,
                threadIndex,
                topN,
                sortBy,
                nameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains.Trim(),
                minMs,
                maxDepth
            });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"读取 Hierarchy 失败：{ex.Message}";
        }
    }
}
