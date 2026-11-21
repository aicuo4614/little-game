using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorRecordTool(ILogger<SkillEditorRecordTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_record")]
    [Description(
        "异步预览录制：可选切阵营/画质后从头播放，按 sampleHz 采样单位与相机世界 TRS，" +
        "生成 schema=kabu.timeline.preview-record 的 JSON。" +
        "untilCutoff=true（默认）自动结束于 min(灰色CutOff, 所有Clip内容终点+余量)，不会硬跑满默认 15s；" +
        "false 则需再调 stop。返回值含完整 record（过大时写 Temp）。超时默认 120s。" +
        "查阵营镜像：分别 record TeamA 与 TeamB 对比 camera/unit 的 pos.x 与 euler.y。")]
    public async Task<string> UnitySkillEditorRecordAsync(
        [Description("TeamA 或 TeamB；空则保持当前")] string? casterTeam = null,
        [Description("Low 或 HQ；空则保持当前")] string? quality = null,
        [Description("采样频率 Hz")] float sampleHz = 30f,
        [Description("是否自动结束（min CutOff 与内容终点）；false 需再 stop")] bool untilCutoff = true)
    {
        logger.LogInformation(
            "SkillEditor record team={Team} quality={Quality} hz={Hz} untilCutoff={Until}",
            casterTeam, quality, sampleHz, untilCutoff);
        try
        {
            var result = await hub.SendRequestAsync(
                "unity.skillEditorRecord",
                new
                {
                    casterTeam,
                    quality,
                    sampleHz,
                    untilCutoff
                },
                timeoutSeconds: 120);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"录制失败：{ex.Message}";
        }
    }
}
