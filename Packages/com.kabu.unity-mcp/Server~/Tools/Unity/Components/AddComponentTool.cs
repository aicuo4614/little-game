using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Components;

[McpServerToolType]
public class AddComponentTool(ILogger<AddComponentTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_add_component")]
    [Description("给场景中的 GameObject 添加组件。componentType 可用短名（Rigidbody）或完整类名。需要 Unity 已连接。可撤销。")]
    [return: Description("添加结果")]
    public async Task<string> UnityAddComponentAsync(
        [Description("组件类型名，如 Rigidbody / BoxCollider / 自定义脚本类名")] string componentType,
        [Description("物体 instanceId；与 path / name 三选一")] int instanceId = 0,
        [Description("物体层级路径")] string? path = null,
        [Description("物体名称（不唯一时请用 path/id）")] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(componentType))
            return "componentType 不能为空。";

        logger.LogInformation("Add component {Type}", componentType);
        try
        {
            var result = await hub.SendRequestAsync("unity.addComponent", new
            {
                componentType = componentType.Trim(),
                instanceId = instanceId == 0 ? (int?)null : instanceId,
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim()
            });
            return FormatMessage(result, "已添加组件。");
        }
        catch (Exception ex)
        {
            return $"添加组件失败：{ex.Message}";
        }
    }

    internal static string FormatMessage(System.Text.Json.JsonElement? result, string fallback)
    {
        if (result == null)
            return "Unity 未返回数据。";
        if (result.Value.TryGetProperty("message", out var msg))
            return msg.GetString() ?? fallback;
        return result.Value.ToString();
    }
}
