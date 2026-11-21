using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Prefabs;

[McpServerToolType]
public class InstantiatePrefabTool(ILogger<InstantiatePrefabTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_instantiate_prefab")]
    [Description("将 Prefab 实例化到当前场景。可设坐标、旋转、缩放、父物体与实例名。")]
    [return: Description("实例化结果")]
    public async Task<string> UnityInstantiatePrefabAsync(
        [Description("Prefab 路径，如 Assets/Prefabs/Enemy.prefab")] string prefabPath,
        [Description("世界坐标 X")] float x = 0,
        [Description("世界坐标 Y")] float y = 0,
        [Description("世界坐标 Z")] float z = 0,
        [Description("欧拉角 X")] float rotX = 0,
        [Description("欧拉角 Y")] float rotY = 0,
        [Description("欧拉角 Z")] float rotZ = 0,
        [Description("缩放 X")] float scaleX = 1,
        [Description("缩放 Y")] float scaleY = 1,
        [Description("缩放 Z")] float scaleZ = 1,
        [Description("实例名称；空则用 Prefab 名")] string? instanceName = null,
        [Description("父物体名称")] string? parent = null,
        [Description("父物体路径")] string? parentPath = null,
        [Description("父物体 instanceId")] int parentInstanceId = 0)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
            return "请提供 prefabPath。";

        logger.LogInformation("Instantiate prefab {Path}", prefabPath);
        try
        {
            var result = await hub.SendRequestAsync("unity.instantiatePrefab", new
            {
                prefabPath = prefabPath.Trim().Replace('\\', '/'),
                position = new { x, y, z },
                rotation = new { x = rotX, y = rotY, z = rotZ },
                scale = new { x = scaleX, y = scaleY, z = scaleZ },
                instanceName = string.IsNullOrWhiteSpace(instanceName) ? null : instanceName.Trim(),
                parent = string.IsNullOrWhiteSpace(parent) ? null : parent.Trim(),
                parentPath = string.IsNullOrWhiteSpace(parentPath) ? null : parentPath.Trim(),
                parentInstanceId = parentInstanceId == 0 ? (int?)null : parentInstanceId
            });
            if (result == null)
                return "Unity 未返回数据。";
            if (result.Value.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "已实例化。";
            return result.Value.ToString();
        }
        catch (Exception ex)
        {
            return $"实例化 Prefab 失败：{ex.Message}";
        }
    }
}
