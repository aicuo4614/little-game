using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerMemoryTakeSnapshotTool(ILogger<ProfilerMemoryTakeSnapshotTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_memory_take_snapshot")]
    [Description("Capture a .snap from the selected Unity Profiler target (Editor or connected Development Player). List/select with unity_profiler_memory_targets first; pass expectedTargetId to reject target changes. Can stall the target. Native capture cannot be cancelled on timeout. Returns file and target metadata, not object analysis.")]
    public async Task<string> UnityProfilerMemoryTakeSnapshotAsync(
        [Description("Optional NEW .snap file path on the Editor host. Existing files are rejected.")] string? snapshotPath = null,
        [Description("Expected selected Profiler target ID; recommended for device captures.")] int? expectedTargetId = null,
        [Description("Bitmask: ManagedObjects=1, NativeObjects=2, NativeAllocations=4, NativeAllocationSites=8, NativeStackTraces=16. Default 7. Stack data requires target support.")] int captureFlags = 7,
        [Description("Capture timeout, 10-600 seconds. Default 180.")] int timeoutSeconds = 180)
    {
        if (timeoutSeconds is < 10 or > 600 || captureFlags <= 0 || (captureFlags & ~31) != 0)
            return "Invalid captureFlags or timeoutSeconds (expected 10-600).";
        logger.LogInformation("Profiler memory take snapshot");
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerMemoryTakeSnapshot",
                new { snapshotPath, expectedTargetId, captureFlags, timeoutSeconds }, timeoutSeconds: timeoutSeconds + 30);
            return result?.ToString() ?? "Unity returned no data.";
        }
        catch (Exception ex) { return $"Memory capture request failed: {ex.Message}"; }
    }
}
