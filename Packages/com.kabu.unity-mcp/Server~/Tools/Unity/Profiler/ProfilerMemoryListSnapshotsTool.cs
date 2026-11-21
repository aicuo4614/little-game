using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerMemoryListSnapshotsTool(ILogger<ProfilerMemoryListSnapshotsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_memory_list_snapshots")]
    [Description(
        "分页列出 .snap 文件。默认搜索 temporaryCachePath/MemoryCaptures 与项目旁 MemoryCaptures。" +
        "可用 searchPath 指定目录。")]
    public async Task<string> UnityProfilerMemoryListSnapshotsAsync(
        [Description("可选搜索目录")] string? searchPath = null,
        [Description("起始偏移，默认 0")] int offset = 0,
        [Description("返回条数，默认 50，最大 200")] int limit = 50)
    {
        logger.LogInformation("Profiler memory list snapshots");
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerMemoryListSnapshots", new
            {
                searchPath = string.IsNullOrWhiteSpace(searchPath) ? null : searchPath.Trim(),
                offset,
                limit
            });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"列出 Snapshot 失败：{ex.Message}";
        }
    }
}
