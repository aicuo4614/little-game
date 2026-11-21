using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Prefabs;

[McpServerToolType]
public class GetPrefabStageTool(ILogger<GetPrefabStageTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_prefab_stage")]
    [Description("查询当前是否处于 Prefab Mode，以及打开的 Prefab 路径。")]
    [return: Description("Stage 信息")]
    public async Task<string> UnityGetPrefabStageAsync()
    {
        logger.LogInformation("Get prefab stage");
        try
        {
            var result = await hub.SendRequestAsync("unity.getPrefabStage", new { });
            if (result == null)
                return "Unity 未返回数据。";
            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"查询 Prefab Stage 失败：{ex.Message}";
        }
    }
}
