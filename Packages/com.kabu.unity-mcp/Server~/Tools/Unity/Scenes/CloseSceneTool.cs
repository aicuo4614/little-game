using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Scenes;

[McpServerToolType]
public class CloseSceneTool(ILogger<CloseSceneTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_close_scene")]
    [Description("关闭已打开的场景。不可关闭唯一打开的场景。sceneIdentifier 为场景名或路径。")]
    public async Task<string> UnityCloseSceneAsync(
        [Description("场景名或路径")] string sceneIdentifier,
        [Description("是否从 Hierarchy 移除，默认 true")] bool removeScene = true)
    {
        if (string.IsNullOrWhiteSpace(sceneIdentifier))
            return "请提供 sceneIdentifier。";
        logger.LogInformation("Close scene {Id}", sceneIdentifier);
        try
        {
            var result = await hub.SendRequestAsync("unity.closeScene", new
            {
                sceneIdentifier = sceneIdentifier.Trim().Replace('\\', '/'),
                removeScene
            });
            if (result?.TryGetProperty("message", out var msg) == true)
                return msg.GetString() ?? "已关闭。";
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex) { return $"关闭场景失败：{ex.Message}"; }
    }
}
