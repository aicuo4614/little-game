using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorStatusTool(ILogger<SkillEditorStatusTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_status")]
    [Description("查询技能编辑器：窗口是否打开、当前 TimelineId/路径、阵营、画质、runningTime、是否播放/录制。")]
    public async Task<string> UnitySkillEditorStatusAsync()
    {
        logger.LogInformation("SkillEditor status");
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorStatus", null);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"查询技能编辑器状态失败：{ex.Message}";
        }
    }
}
