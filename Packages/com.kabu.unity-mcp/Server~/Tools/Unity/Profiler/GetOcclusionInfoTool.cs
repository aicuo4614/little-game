using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class GetOcclusionInfoTool(ILogger<GetOcclusionInfoTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_occlusion_info")]
    [Description(
        "只读查询当前激活场景的遮挡剔除结论（schema=kabu.perf.occlusion/v1）：" +
        "OcclusionArea/Portal 数量、OcclusionCullingData 资产线索、conclusion/gaps。" +
        "无副作用；缺 bake 时 conclusion 可能为 unknown_or_disabled 并填 gaps，不算工具失败。")]
    public async Task<string> UnityGetOcclusionInfoAsync()
    {
        logger.LogInformation("get occlusion info");
        try
        {
            var result = await hub.SendRequestAsync("unity.getOcclusionInfo", null, timeoutSeconds: 30);
            if (result == null) return "Unity 未返回数据。";
            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"occlusion 查询失败：{ex.Message}";
        }
    }
}
