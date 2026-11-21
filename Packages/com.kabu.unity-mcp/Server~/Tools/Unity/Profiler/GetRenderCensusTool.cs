using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class GetRenderCensusTool(ILogger<GetRenderCensusTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_render_census")]
    [Description(
        "只读场景渲染量级普查（schema=kabu.perf.render-census/v1）：" +
        "灯光（实时/混合/烘焙、阴影开启数、按类型）与 Renderer（Mesh/Skinned、启用数、唯一材质数）。" +
        "无副作用。")]
    public async Task<string> UnityGetRenderCensusAsync()
    {
        logger.LogInformation("get render census");
        try
        {
            var result = await hub.SendRequestAsync("unity.getRenderCensus", null, timeoutSeconds: 30);
            if (result == null) return "Unity 未返回数据。";
            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"render census 查询失败：{ex.Message}";
        }
    }
}
