using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerSetAreasTool(ILogger<ProfilerSetAreasTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_set_areas")]
    [Description(
        "按名称开关 ProfilerArea。areasJson 示例：{\"CPU\":true,\"GPU\":false,\"Memory\":true,\"Rendering\":true}。" +
        "区域名与 Unity ProfilerArea 枚举一致。")]
    public async Task<string> UnityProfilerSetAreasAsync(
        [Description("JSON 对象：areaName → bool")] string areasJson)
    {
        logger.LogInformation("Profiler set areas");
        try
        {
            if (string.IsNullOrWhiteSpace(areasJson))
                return "请提供 areasJson。";

            var areas = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(areasJson);
            var result = await hub.SendRequestAsync("unity.profilerSetAreas", new { areas });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"设置 Profiler 区域失败：{ex.Message}";
        }
    }
}
