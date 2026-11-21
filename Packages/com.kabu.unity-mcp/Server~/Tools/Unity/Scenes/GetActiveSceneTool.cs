using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Scenes;

[McpServerToolType]
public class GetActiveSceneTool(ILogger<GetActiveSceneTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_active_scene")]
    [Description("获取当前激活场景的名称、路径、是否脏、根物体数量等。")]
    public async Task<string> UnityGetActiveSceneAsync()
    {
        logger.LogInformation("Get active scene");
        try
        {
            var result = await hub.SendRequestAsync("unity.getActiveScene", null);
            if (result == null) return "Unity 未返回数据。";
            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) { return $"查询失败：{ex.Message}"; }
    }
}
