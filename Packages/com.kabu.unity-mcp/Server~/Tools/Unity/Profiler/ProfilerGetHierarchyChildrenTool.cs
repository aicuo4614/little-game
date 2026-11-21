using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerGetHierarchyChildrenTool(ILogger<ProfilerGetHierarchyChildrenTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_get_hierarchy_children")]
    [Description(
        "按 get_hierarchy 返回的 itemId 分页展开子节点。" +
        "支持 offset/limit/nameContains。须先 enable 并有录制帧。")]
    public async Task<string> UnityProfilerGetHierarchyChildrenAsync(
        [Description("父节点 id（来自 get_hierarchy）")] int itemId,
        [Description("帧索引；省略则用 lastFrameIndex")] int? frameIndex = null,
        [Description("线程索引，默认 0")] int threadIndex = 0,
        [Description("起始偏移，默认 0")] int offset = 0,
        [Description("返回条数，默认 50，最大 200")] int limit = 50,
        [Description("按名称子串过滤")] string? nameContains = null)
    {
        logger.LogInformation("Profiler get hierarchy children itemId={ItemId}", itemId);
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerGetHierarchyChildren", new
            {
                itemId,
                frameIndex,
                threadIndex,
                offset,
                limit,
                nameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains.Trim()
            });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"读取 Hierarchy 子节点失败：{ex.Message}";
        }
    }
}
