using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorPlayTool(ILogger<SkillEditorPlayTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_play")]
    [Description("开始技能预览播放。fromStart=true 时先停到开头再播。")]
    public async Task<string> UnitySkillEditorPlayAsync(
        [Description("是否从头播放")] bool fromStart = false)
    {
        logger.LogInformation("SkillEditor play fromStart={FromStart}", fromStart);
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorPlay", new { fromStart });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"播放失败：{ex.Message}";
        }
    }
}
