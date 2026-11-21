using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.GameObjects;

[McpServerToolType]
public class ModifyGameObjectTool(ILogger<ModifyGameObjectTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_modify_game_object")]
    [Description("修改 GameObject 常用字段：名称、激活、tag、layer、位置/旋转/缩放。只需传要改的字段。可用 Ctrl+Z 撤销。")]
    [return: Description("修改结果")]
    public async Task<string> UnityModifyGameObjectAsync(
        [Description("目标 instanceId")] int instanceId = 0,
        [Description("目标层级路径")] string? path = null,
        [Description("目标当前名称（用于定位）")] string? name = null,
        [Description("新名称")] string? newName = null,
        [Description("是否激活；不传则不改")] bool? active = null,
        [Description("Tag；不传则不改")] string? tag = null,
        [Description("Layer 索引；-1 表示不改")] int layer = -1,
        [Description("是否提供 position")] bool setPosition = false,
        [Description("坐标 X")] float x = 0,
        [Description("坐标 Y")] float y = 0,
        [Description("坐标 Z")] float z = 0,
        [Description("是否提供 rotation")] bool setRotation = false,
        [Description("欧拉角 X")] float rotX = 0,
        [Description("欧拉角 Y")] float rotY = 0,
        [Description("欧拉角 Z")] float rotZ = 0,
        [Description("是否提供 scale")] bool setScale = false,
        [Description("缩放 X")] float scaleX = 1,
        [Description("缩放 Y")] float scaleY = 1,
        [Description("缩放 Z")] float scaleZ = 1,
        [Description("true=按本地坐标改 transform")] bool isLocalSpace = false)
    {
        logger.LogInformation("Modify GameObject id={Id}", instanceId);
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["instanceId"] = instanceId == 0 ? null : instanceId,
                ["path"] = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                ["name"] = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                ["isLocalSpace"] = isLocalSpace
            };

            if (!string.IsNullOrWhiteSpace(newName))
                payload["newName"] = newName.Trim();
            if (active.HasValue)
                payload["active"] = active.Value;
            if (!string.IsNullOrWhiteSpace(tag))
                payload["tag"] = tag.Trim();
            if (layer >= 0)
                payload["layer"] = layer;
            if (setPosition)
                payload["position"] = new { x, y, z };
            if (setRotation)
                payload["rotation"] = new { x = rotX, y = rotY, z = rotZ };
            if (setScale)
                payload["scale"] = new { x = scaleX, y = scaleY, z = scaleZ };

            var result = await hub.SendRequestAsync("unity.modifyGameObject", payload);
            if (result == null)
                return "已修改，但 Unity 未返回数据。";

            var el = result.Value;
            var changed = "?";
            if (el.TryGetProperty("changed", out var ch) && ch.ValueKind == JsonValueKind.Array)
                changed = string.Join(", ", ch.EnumerateArray().Select(c => c.GetString()));

            var go = el.TryGetProperty("gameObject", out var g) ? g : el;
            return $"已修改字段：{changed}\n{GameObjectToolFormat.FormatInfo("当前", go)}";
        }
        catch (Exception ex)
        {
            return $"修改失败：{ex.Message}";
        }
    }
}
