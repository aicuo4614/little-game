using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorSnapshotTool(ILogger<SkillEditorSnapshotTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_snapshot")]
    [Description(
        "采集当前帧预览场上单位与战斗相机的世界 pos/euler/scale（相机含 fov）。" +
        "与 preview-record schema 的 catalog/samples 同形，用于定点排查。")]
    public async Task<string> UnitySkillEditorSnapshotAsync()
    {
        logger.LogInformation("SkillEditor snapshot");
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorSnapshot", null);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"快照失败：{ex.Message}";
        }
    }
}
