using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class GetPostProcessStackTool(ILogger<GetPostProcessStackTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_post_process_stack")]
    [Description(
        "只读盘点激活场景后处理相关组件（schema=kabu.perf.postprocess/v1）：" +
        "URP Volume / PostProcess / Beautify 等按类型名匹配，返回 typeCounts 与条目列表。" +
        "无副作用；不依赖 Beautify 程序集引用。")]
    public async Task<string> UnityGetPostProcessStackAsync(
        [Description("最多返回条目数，默认 80，最大 300")] int maxItems = 80)
    {
        logger.LogInformation("get post process stack");
        try
        {
            var result = await hub.SendRequestAsync(
                "unity.getPostProcessStack",
                new { maxItems },
                timeoutSeconds: 30);
            if (result == null) return "Unity 未返回数据。";
            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"后处理栈查询失败：{ex.Message}";
        }
    }
}
