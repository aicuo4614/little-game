using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Scenes;

[McpServerToolType]
public class SaveSceneTool(ILogger<SaveSceneTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_save_scene")]
    [Description("保存已打开场景。不传 sceneName 则保存激活场景；新场景需提供 path。")]
    [return: Description("保存结果")]
    public async Task<string> UnitySaveSceneAsync(
        [Description("已打开场景名称；空=激活场景")] string? sceneName = null,
        [Description("保存路径（.unity）；空=写回原路径")] string? path = null)
    {
        logger.LogInformation("Save scene {Name}", sceneName);
        try
        {
            var result = await hub.SendRequestAsync("unity.saveScene", new
            {
                sceneName = string.IsNullOrWhiteSpace(sceneName) ? null : sceneName.Trim(),
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim().Replace('\\', '/')
            });
            if (result == null)
                return "已请求保存，但 Unity 未返回数据。";

            var el = result.Value;
            if (el.TryGetProperty("scene", out var scene))
            {
                var name = scene.TryGetProperty("name", out var n) ? n.GetString() : "?";
                var p = scene.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : "?";
                return $"已保存场景「{name}」→ {p}";
            }
            return "场景已保存。";
        }
        catch (Exception ex)
        {
            return $"保存场景失败：{ex.Message}";
        }
    }
}
