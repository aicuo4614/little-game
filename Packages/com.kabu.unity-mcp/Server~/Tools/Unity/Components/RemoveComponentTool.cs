using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Components;

[McpServerToolType]
public class RemoveComponentTool(ILogger<RemoveComponentTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_remove_component")]
    [Description("从 GameObject 移除组件（不可移除 Transform）。需要 Unity 已连接。可撤销。")]
    [return: Description("移除结果")]
    public async Task<string> UnityRemoveComponentAsync(
        [Description("组件类型名")] string componentType,
        [Description("物体 instanceId")] int instanceId = 0,
        [Description("物体层级路径")] string? path = null,
        [Description("物体名称")] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(componentType))
            return "componentType 不能为空。";

        logger.LogInformation("Remove component {Type}", componentType);
        try
        {
            var result = await hub.SendRequestAsync("unity.removeComponent", new
            {
                componentType = componentType.Trim(),
                instanceId = instanceId == 0 ? (int?)null : instanceId,
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim()
            });
            return AddComponentTool.FormatMessage(result, "已移除组件。");
        }
        catch (Exception ex)
        {
            return $"移除组件失败：{ex.Message}";
        }
    }
}
