using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class EnterPlayModeTool(ILogger<EnterPlayModeTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_enter_play_mode")]
    [Description(
        "进入 Play Mode，等到 Playing 且 Player 帧推进（默认约 15 帧）后返回；超时约 45s。" +
        "成功只表示 Play 在跑，不表示场景一定有可画内容（空场景 Batches 可为 0）。" +
        "有无有效渲染由 unity_perf_capture_frame 的 strictBasics 判断。" +
        "默认 disableDomainReload=true，避免 MCP WebSocket 因 Domain Reload 断开。")]
    public async Task<string> UnityEnterPlayModeAsync(
        [Description("默认 true：临时 DisableDomainReload。false：完整 Domain Reload。")] bool disableDomainReload = true,
        [Description("EnteredPlayMode 后再等多少个 Time.frameCount 增量，默认 15；0=不等帧。")] int settleFrames = 15)
    {
        logger.LogInformation(
            "Enter play mode (disableDomainReload={Disable}, settleFrames={Frames})",
            disableDomainReload,
            settleFrames);
        try
        {
            var result = await hub.SendRequestAsync(
                "unity.enterPlayMode",
                new { disableDomainReload, settleFrames },
                timeoutSeconds: 60);
            // 返回完整 JSON，便于确认 entered/isPlaying/settle，而不只看 message
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex) { return $"进入 Play Mode 失败：{ex.Message}"; }
    }
}
