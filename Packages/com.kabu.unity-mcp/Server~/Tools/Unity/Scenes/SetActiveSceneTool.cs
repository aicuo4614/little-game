using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Scenes;

[McpServerToolType]
public class SetActiveSceneTool(ILogger<SetActiveSceneTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_set_active_scene")]
    [Description("将已打开的场景设为激活场景。sceneIdentifier 可为场景名或 Assets/...unity 路径。")]
    public async Task<string> UnitySetActiveSceneAsync(
        [Description("场景名或路径")] string sceneIdentifier)
    {
        if (string.IsNullOrWhiteSpace(sceneIdentifier))
            return "请提供 sceneIdentifier。";
        logger.LogInformation("Set active scene {Id}", sceneIdentifier);
        try
        {
            var result = await hub.SendRequestAsync("unity.setActiveScene", new
            {
                sceneIdentifier = sceneIdentifier.Trim().Replace('\\', '/')
            });
            if (result?.TryGetProperty("message", out var msg) == true)
                return msg.GetString() ?? "已设置。";
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex) { return $"设置激活场景失败：{ex.Message}"; }
    }
}
