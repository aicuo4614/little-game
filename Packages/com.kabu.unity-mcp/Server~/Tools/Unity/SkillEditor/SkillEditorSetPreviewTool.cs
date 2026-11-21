using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorSetPreviewTool(ILogger<SkillEditorSetPreviewTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_set_preview")]
    [Description(
        "设置技能编辑器预览选项（不写技能 JSON）。casterTeam=TeamA|TeamB；quality=Low|HQ。" +
        "查阵营镜像 bug 时先切 TeamB 再 record/snapshot。副作用：重建施法者/对面站位。")]
    public async Task<string> UnitySkillEditorSetPreviewAsync(
        [Description("TeamA 或 TeamB；空则不变")] string? casterTeam = null,
        [Description("Low 或 HQ；空则不变")] string? quality = null)
    {
        logger.LogInformation("SkillEditor setPreview team={Team} quality={Quality}", casterTeam, quality);
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorSetPreview", new
            {
                casterTeam,
                quality
            });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"设置预览失败：{ex.Message}";
        }
    }
}
