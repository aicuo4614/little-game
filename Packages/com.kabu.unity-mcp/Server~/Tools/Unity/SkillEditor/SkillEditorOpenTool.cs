using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorOpenTool(ILogger<SkillEditorOpenTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_open")]
    [Description(
        "打开技能时间轴编辑器：切换到 TimelineEditor 场景并打开窗口。" +
        "需要工程侧已注册 SkillEditor Host（美术工程 TimelineEditor）。" +
        "副作用：可能切换当前场景。")]
    public async Task<string> UnitySkillEditorOpenAsync()
    {
        logger.LogInformation("SkillEditor open");
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorOpen", null);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"打开技能编辑器失败：{ex.Message}";
        }
    }
}
