using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Prefabs;

[McpServerToolType]
public class GetPrefabInfoTool(ILogger<GetPrefabInfoTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_prefab_info")]
    [Description("查询 Prefab：传 prefabPath 查资源；或传 instanceId/path/name 查场景实例状态。")]
    [return: Description("Prefab 信息 JSON")]
    public async Task<string> UnityGetPrefabInfoAsync(
        [Description("Prefab 资源路径")] string? prefabPath = null,
        [Description("场景物体 instanceId")] int instanceId = 0,
        [Description("场景物体路径")] string? path = null,
        [Description("场景物体名称")] string? name = null)
    {
        logger.LogInformation("Get prefab info");
        try
        {
            var result = await hub.SendRequestAsync("unity.getPrefabInfo", new
            {
                prefabPath = string.IsNullOrWhiteSpace(prefabPath) ? null : prefabPath.Trim().Replace('\\', '/'),
                instanceId = instanceId == 0 ? (int?)null : instanceId,
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim()
            });
            if (result == null)
                return "Unity 未返回数据。";
            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"查询 Prefab 失败：{ex.Message}";
        }
    }
}
