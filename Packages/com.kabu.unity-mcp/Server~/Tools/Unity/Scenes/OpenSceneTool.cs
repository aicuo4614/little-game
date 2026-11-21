using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Scenes;

[McpServerToolType]
public class OpenSceneTool(ILogger<OpenSceneTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_open_scene")]
    [Description("打开项目中的场景资源。mode=Single（默认，关闭当前）或 Additive（叠加打开）。")]
    [return: Description("打开结果")]
    public async Task<string> UnityOpenSceneAsync(
        [Description("场景资源路径，如 Assets/Scenes/Sample.unity")] string path,
        [Description("Single 或 Additive，默认 Single")] string mode = "Single")
    {
        if (string.IsNullOrWhiteSpace(path))
            return "请提供场景路径。";

        logger.LogInformation("Open scene {Path} mode={Mode}", path, mode);
        try
        {
            var result = await hub.SendRequestAsync("unity.openScene", new
            {
                path = path.Trim().Replace('\\', '/'),
                mode
            });
            if (result == null)
                return "已请求打开，但 Unity 未返回数据。";

            var el = result.Value;
            var opened = el.TryGetProperty("opened", out var o) ? o : el;
            var name = opened.TryGetProperty("name", out var n) ? n.GetString() : "?";
            var p = opened.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : path;
            return $"已打开场景「{name}」\n路径：{p}\n模式：{mode}";
        }
        catch (Exception ex)
        {
            return $"打开场景失败：{ex.Message}";
        }
    }
}
