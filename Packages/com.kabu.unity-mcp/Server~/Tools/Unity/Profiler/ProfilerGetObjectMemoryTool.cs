using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerGetObjectMemoryTool(ILogger<ProfilerGetObjectMemoryTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_get_object_memory")]
    [Description(
        "查询单个场景 GameObject 或项目资源的运行时内存（Profiler.GetRuntimeMemorySizeLong）。" +
        "提供 instanceId / 场景 path / name，或 Assets/... 资源路径之一。")]
    public async Task<string> UnityProfilerGetObjectMemoryAsync(
        [Description("场景物体 instanceId")] int? instanceId = null,
        [Description("场景层级路径，或 Assets/... 资源路径")] string? path = null,
        [Description("场景物体名称（不唯一时请改用 path/instanceId）")] string? name = null)
    {
        logger.LogInformation("Profiler get object memory");
        try
        {
            var result = await hub.SendRequestAsync("unity.profilerGetObjectMemory", new
            {
                instanceId,
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim()
            });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"读取对象内存失败：{ex.Message}";
        }
    }
}
