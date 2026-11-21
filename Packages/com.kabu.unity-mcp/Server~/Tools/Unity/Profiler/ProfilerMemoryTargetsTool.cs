using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerMemoryTargetsTool(UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_memory_targets")]
    [Description("List available Unity Profiler targets and capture capability/busy state, or select a target by ID. Selection affects all clients of this Editor. Connect a Development Player in Unity Profiler first. Selection is blocked while a native memory capture is active.")]
    public async Task<string> UnityProfilerMemoryTargetsAsync(
        [Description("Omit to list targets. Supply an ID from that list to select Editor or a connected Player.")] int? targetId = null)
    {
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerMemoryTargets", new { targetId });
            return result?.ToString() ?? "Unity returned no data.";
        }
        catch (Exception ex) { return $"Memory target request failed: {ex.Message}"; }
    }
}
