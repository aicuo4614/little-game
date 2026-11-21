using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorStopTool(ILogger<SkillEditorStopTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_stop")]
    [Description(
        "停止预览并复位播放头/场景。" +
        "若正在录制，会结束录制并把预览 record JSON 放入返回值（同时复制剪切板）。")]
    public async Task<string> UnitySkillEditorStopAsync()
    {
        logger.LogInformation("SkillEditor stop");
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorStop", null, timeoutSeconds: 60);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"停止失败：{ex.Message}";
        }
    }
}
