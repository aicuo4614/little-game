using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorListClipsTool(ILogger<SkillEditorListClipsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_list_clips")]
    [Description("分页列出当前已加载技能的 Clip 摘要：clipKey、type、typeName、label、start、delay、duration。")]
    public async Task<string> UnitySkillEditorListClipsAsync(
        [Description("偏移")] int offset = 0,
        [Description("每页条数")] int limit = 50)
    {
        logger.LogInformation("SkillEditor listClips offset={Offset} limit={Limit}", offset, limit);
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorListClips", new { offset, limit });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"列出 Clip 失败：{ex.Message}";
        }
    }
}
