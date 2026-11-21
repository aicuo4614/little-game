using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.GameObjects;

[McpServerToolType]
public class FindGameObjectTool(ILogger<FindGameObjectTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_find_game_object")]
    [Description("按 instanceId / 层级路径 / 名称查找当前激活场景中的 GameObject（只读）。名称重名时请改用 path 或 instanceId。")]
    [return: Description("物体详情文本")]
    public async Task<string> UnityFindGameObjectAsync(
        [Description("Unity instanceId，优先使用")] int instanceId = 0,
        [Description("层级路径，如 Canvas/Panel/Button")] string? path = null,
        [Description("物体名称（唯一时可用）")] string? name = null,
        [Description("是否包含组件类型列表，默认 true")] bool includeComponents = true)
    {
        logger.LogInformation("Find GameObject id={Id} path={Path} name={Name}", instanceId, path, name);
        try
        {
            var result = await hub.SendRequestAsync("unity.findGameObject", new
            {
                instanceId = instanceId == 0 ? (int?)null : instanceId,
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                includeComponents
            });
            return GameObjectToolFormat.FormatDetail(result);
        }
        catch (Exception ex)
        {
            return $"查找失败：{ex.Message}";
        }
    }
}
