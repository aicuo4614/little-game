using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorPauseTool(ILogger<SkillEditorPauseTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_pause")]
    [Description("暂停技能预览播放（不复位场景；录制中暂停不会结束录制）。")]
    public async Task<string> UnitySkillEditorPauseAsync()
    {
        logger.LogInformation("SkillEditor pause");
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorPause", null);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"暂停失败：{ex.Message}";
        }
    }
}
