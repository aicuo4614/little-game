using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerMemoryCompareSnapshotsTool(ILogger<ProfilerMemoryCompareSnapshotsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_memory_compare_snapshots")]
    [Description("Compare .snap FILE metadata (size, creation time, optional MCP capture provenance). This does not parse objects/types/references. File-size delta is NOT allocated-memory delta and cannot establish a memory leak. Use the Unity Memory Profiler package for object analysis.")]
    public async Task<string> UnityProfilerMemoryCompareSnapshotsAsync(
        [Description("第一份 snapshot 路径")] string snapshotA,
        [Description("第二份 snapshot 路径")] string snapshotB)
    {
        logger.LogInformation("Profiler memory compare snapshots");
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerMemoryCompareSnapshots", new
            {
                snapshotA,
                snapshotB
            });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"对比 Snapshot 失败：{ex.Message}";
        }
    }
}
