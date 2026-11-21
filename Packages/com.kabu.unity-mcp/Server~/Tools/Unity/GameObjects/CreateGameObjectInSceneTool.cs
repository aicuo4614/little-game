using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.GameObjects;

[McpServerToolType]
public class CreateGameObjectInSceneTool(ILogger<CreateGameObjectInSceneTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_create_game_object_in_scene")]
    [Description("在指定场景资源中创建 GameObject。场景未打开时默认 Additive 打开；创建后会恢复原先激活场景。可用 Ctrl+Z 撤销。")]
    public async Task<string> UnityCreateGameObjectInSceneAsync(
        [Description("场景资源路径，如 Assets/Scenes/Sample.unity")] string scenePath,
        [Description("新物体名称")] string name,
        [Description("世界或本地坐标 X，默认 0")] float x = 0,
        [Description("世界或本地坐标 Y，默认 0")] float y = 0,
        [Description("世界或本地坐标 Z，默认 0")] float z = 0,
        [Description("欧拉角 X（度），默认 0")] float rotX = 0,
        [Description("欧拉角 Y（度），默认 0")] float rotY = 0,
        [Description("欧拉角 Z（度），默认 0")] float rotZ = 0,
        [Description("缩放 X，默认 1")] float scaleX = 1,
        [Description("缩放 Y，默认 1")] float scaleY = 1,
        [Description("缩放 Z，默认 1")] float scaleZ = 1,
        [Description("true=按本地坐标应用 transform")] bool isLocalSpace = false,
        [Description("父物体名称")] string? parent = null,
        [Description("父物体层级路径")] string? parentPath = null,
        [Description("父物体 instanceId")] int parentInstanceId = 0,
        [Description("基础几何体：Cube / Sphere 等")] string? primitiveType = null,
        [Description("场景未打开时是否 Additive 打开，默认 true")] bool openIfClosed = true)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return "请提供 scenePath。";
        if (string.IsNullOrWhiteSpace(name))
            return "名称不能为空。";

        name = name.Trim();
        logger.LogInformation("Create GameObject {Name} in scene {Path}", name, scenePath);

        try
        {
            var result = await hub.SendRequestAsync("unity.createGameObjectInScene", new
            {
                scenePath = scenePath.Trim().Replace('\\', '/'),
                name,
                position = new { x, y, z },
                rotation = new { x = rotX, y = rotY, z = rotZ },
                scale = new { x = scaleX, y = scaleY, z = scaleZ },
                isLocalSpace,
                parent = string.IsNullOrWhiteSpace(parent) ? null : parent.Trim(),
                parentPath = string.IsNullOrWhiteSpace(parentPath) ? null : parentPath.Trim(),
                parentInstanceId = parentInstanceId == 0 ? (int?)null : parentInstanceId,
                primitiveType = string.IsNullOrWhiteSpace(primitiveType) ? null : primitiveType.Trim(),
                openIfClosed
            });

            if (result?.TryGetProperty("message", out var msg) == true)
            {
                var go = GameObjectToolFormat.FormatInfo("物体",
                    result.Value.TryGetProperty("gameObject", out var goEl) ? goEl : result);
                return $"{msg.GetString()}\n{go}";
            }

            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"在场景中创建失败：{ex.Message}";
        }
    }
}
