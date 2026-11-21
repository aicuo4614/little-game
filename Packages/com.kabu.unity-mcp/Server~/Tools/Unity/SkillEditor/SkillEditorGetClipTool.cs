using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorGetClipTool(ILogger<SkillEditorGetClipTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_get_clip")]
    [Description("按 clipKey 取单条 Clip 配置摘要（含运镜 MirrorByTeam 等关键字段与 configJson）。")]
    public async Task<string> UnitySkillEditorGetClipAsync(
        [Description("ClipKey")] string clipKey)
    {
        if (string.IsNullOrWhiteSpace(clipKey))
        {
            return "请提供 clipKey。";
        }

        logger.LogInformation("SkillEditor getClip {Key}", clipKey);
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorGetClip", new { clipKey = clipKey.Trim() });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"获取 Clip 失败：{ex.Message}";
        }
    }
}
