using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.GameObjects;

[McpServerToolType]
public class DuplicateGameObjectTool(ILogger<DuplicateGameObjectTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_duplicate_game_object")]
    [Description("复制场景中的 GameObject（含层级与组件）。可用 Ctrl+Z 撤销。")]
    [return: Description("副本信息")]
    public async Task<string> UnityDuplicateGameObjectAsync(
        [Description("Unity instanceId")] int instanceId = 0,
        [Description("层级路径")] string? path = null,
        [Description("物体名称（唯一时可用）")] string? name = null)
    {
        logger.LogInformation("Duplicate GameObject id={Id}", instanceId);
        try
        {
            var result = await hub.SendRequestAsync("unity.duplicateGameObject", new
            {
                instanceId = instanceId == 0 ? (int?)null : instanceId,
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim()
            });
            return GameObjectToolFormat.FormatInfo("已复制，新物体", result);
        }
        catch (Exception ex)
        {
            return $"复制失败：{ex.Message}";
        }
    }
}
