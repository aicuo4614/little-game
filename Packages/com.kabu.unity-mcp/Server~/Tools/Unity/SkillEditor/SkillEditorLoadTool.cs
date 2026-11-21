using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorLoadTool(ILogger<SkillEditorLoadTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_load")]
    [Description(
        "加载技能 Timeline JSON。pathOrTimelineId 可为 Assets/.../xxx.json 完整路径，或 TimelineId（如 test_skill6，在默认目录 Package_Unit/Timeline/Base 下查找）。" +
        "副作用：替换当前时间轴并停止预览。")]
    public async Task<string> UnitySkillEditorLoadAsync(
        [Description("资源路径或 TimelineId")] string pathOrTimelineId)
    {
        if (string.IsNullOrWhiteSpace(pathOrTimelineId))
        {
            return "请提供 pathOrTimelineId。";
        }

        logger.LogInformation("SkillEditor load {Id}", pathOrTimelineId);
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorLoad", new
            {
                pathOrTimelineId = pathOrTimelineId.Trim()
            });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"加载技能失败：{ex.Message}";
        }
    }
}
