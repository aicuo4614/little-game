using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Prefabs;

[McpServerToolType]
public class CreatePrefabTool(ILogger<CreatePrefabTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_create_prefab")]
    [Description("从场景 GameObject 创建 Prefab 资源。prefabPath 如 Assets/Prefabs/Enemy.prefab。createVariant=true 时源物体必须是 Prefab 实例。")]
    [return: Description("创建结果")]
    public async Task<string> UnityCreatePrefabAsync(
        [Description("目标 Prefab 路径，如 Assets/Prefabs/Foo.prefab")] string prefabPath,
        [Description("源物体 instanceId")] int instanceId = 0,
        [Description("源物体层级路径")] string? path = null,
        [Description("源物体名称")] string? name = null,
        [Description("是否创建 Prefab Variant")] bool createVariant = false)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
            return "请提供 prefabPath。";

        logger.LogInformation("Create prefab {Path}", prefabPath);
        try
        {
            var result = await hub.SendRequestAsync("unity.createPrefab", new
            {
                prefabPath = prefabPath.Trim().Replace('\\', '/'),
                instanceId = instanceId == 0 ? (int?)null : instanceId,
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                createVariant
            });
            if (result == null)
                return "Unity 未返回数据。";
            if (result.Value.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "已创建 Prefab。";
            return result.Value.ToString();
        }
        catch (Exception ex)
        {
            return $"创建 Prefab 失败：{ex.Message}";
        }
    }
}
