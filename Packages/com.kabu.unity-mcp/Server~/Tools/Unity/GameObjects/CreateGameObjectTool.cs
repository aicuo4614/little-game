using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.GameObjects;

[McpServerToolType]
public class CreateGameObjectTool(ILogger<CreateGameObjectTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_create_game_object")]
    [Description("在当前激活场景创建一个 GameObject。可选坐标、旋转、缩放、父物体、基础几何体。需要 Unity 已连接。可用 Ctrl+Z 撤销。")]
    [return: Description("创建结果，含 name / path / instanceId")]
    public async Task<string> UnityCreateGameObjectAsync(
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
        [Description("父物体名称；与 parentPath / parentInstanceId 三选一")] string? parent = null,
        [Description("父物体层级路径，如 Canvas/Panel")] string? parentPath = null,
        [Description("父物体 instanceId")] int parentInstanceId = 0,
        [Description("基础几何体：Cube / Sphere / Capsule / Cylinder / Plane / Quad，空则空物体")] string? primitiveType = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "名称不能为空。";

        name = name.Trim();
        logger.LogInformation("Creating GameObject {Name}", name);

        try
        {
            var result = await hub.SendRequestAsync("unity.createGameObject", new
            {
                name,
                position = new { x, y, z },
                rotation = new { x = rotX, y = rotY, z = rotZ },
                scale = new { x = scaleX, y = scaleY, z = scaleZ },
                isLocalSpace,
                parent = string.IsNullOrWhiteSpace(parent) ? null : parent.Trim(),
                parentPath = string.IsNullOrWhiteSpace(parentPath) ? null : parentPath.Trim(),
                parentInstanceId = parentInstanceId == 0 ? (int?)null : parentInstanceId,
                primitiveType = string.IsNullOrWhiteSpace(primitiveType) ? null : primitiveType.Trim()
            });

            return GameObjectToolFormat.FormatInfo("已创建", result);
        }
        catch (Exception ex)
        {
            return $"创建失败：{ex.Message}";
        }
    }
}
