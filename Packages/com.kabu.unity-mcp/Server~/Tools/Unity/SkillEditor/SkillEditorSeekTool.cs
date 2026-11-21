using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorSeekTool(ILogger<SkillEditorSeekTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_seek")]
    [Description(
        "停播放后把技能编辑器播放头滑到目标时间（与点时间轴标尺相同，不瞬移，保证 Clip 进出回调连续触发）。" +
        "clipKey 非空时滑到该 Clip 内容起点（Start+Delay）；否则用 time 秒。" +
        "滑动完成并再等采样帧后才返回，适合再调 snapshot。")]
    public async Task<string> UnitySkillEditorSeekAsync(
        [Description("时间轴时间（秒）；提供 clipKey 时忽略")] float time = 0,
        [Description("可选。ClipKey，定位到该条内容起点")] string clipKey = null)
    {
        logger.LogInformation("SkillEditor seek {Time} clip={Clip}", time, clipKey);
        try
        {
            var result = await hub.SendRequestAsync(
                "unity.skillEditorSeek",
                new { time, clipKey },
                timeoutSeconds: 60);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"Seek 失败：{ex.Message}";
        }
    }
}
