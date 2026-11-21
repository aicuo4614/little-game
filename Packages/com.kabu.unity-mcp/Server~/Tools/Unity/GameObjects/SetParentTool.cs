using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.GameObjects;

[McpServerToolType]
public class SetParentTool(ILogger<SetParentTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_set_parent")]
    [Description("设置 GameObject 的父物体；unparent=true 时挂到场景根。可用 Ctrl+Z 撤销。")]
    [return: Description("操作后物体信息")]
    public async Task<string> UnitySetParentAsync(
        [Description("目标 instanceId")] int instanceId = 0,
        [Description("目标层级路径")] string? path = null,
        [Description("目标名称")] string? name = null,
        [Description("新父物体名称")] string? parent = null,
        [Description("新父物体路径")] string? parentPath = null,
        [Description("新父物体 instanceId")] int parentInstanceId = 0,
        [Description("true=解除父子，挂到场景根")] bool unparent = false,
        [Description("true=保持世界坐标（默认）")] bool worldPositionStays = true)
    {
        logger.LogInformation("SetParent id={Id} unparent={Unparent}", instanceId, unparent);
        try
        {
            var result = await hub.SendRequestAsync("unity.setParent", new
            {
                instanceId = instanceId == 0 ? (int?)null : instanceId,
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                parent = string.IsNullOrWhiteSpace(parent) ? null : parent.Trim(),
                parentPath = string.IsNullOrWhiteSpace(parentPath) ? null : parentPath.Trim(),
                parentInstanceId = parentInstanceId == 0 ? (int?)null : parentInstanceId,
                unparent,
                worldPositionStays
            });
            return GameObjectToolFormat.FormatInfo("已设置父物体", result);
        }
        catch (Exception ex)
        {
            return $"设置父物体失败：{ex.Message}";
        }
    }
}
