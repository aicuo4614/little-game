using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.GameObjects;

[McpServerToolType]
public class DestroyGameObjectTool(ILogger<DestroyGameObjectTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_destroy_game_object")]
    [Description("销毁场景中的 GameObject（含子物体）。可用 Ctrl+Z 撤销。需先用 find / list 确认目标。")]
    [return: Description("销毁结果")]
    public async Task<string> UnityDestroyGameObjectAsync(
        [Description("Unity instanceId")] int instanceId = 0,
        [Description("层级路径")] string? path = null,
        [Description("物体名称（唯一时可用）")] string? name = null)
    {
        logger.LogInformation("Destroy GameObject id={Id}", instanceId);
        try
        {
            var result = await hub.SendRequestAsync("unity.destroyGameObject", new
            {
                instanceId = instanceId == 0 ? (int?)null : instanceId,
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim()
            });

            if (result == null)
                return "已请求销毁，但 Unity 未返回数据。";

            var el = result.Value;
            if (el.TryGetProperty("destroyed", out var d))
                return GameObjectToolFormat.FormatInfo("已销毁", d);
            return GameObjectToolFormat.FormatInfo("已销毁", result);
        }
        catch (Exception ex)
        {
            return $"销毁失败：{ex.Message}";
        }
    }
}
