using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class PerfCaptureFrameTool(ILogger<PerfCaptureFrameTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_perf_capture_frame")]
    [Description(
        "统一采集一帧性能摘要 JSON（schema=kabu.perf.frame-capture/v1）。" +
        "默认 sections：basics + profile + frameDebugger；overdraw 始终占位 unavailable。" +
        "副作用：若未在 Play 会自动进入 Play（临时 DisableDomainReload）；聚焦 Game；可能 enable Profiler；" +
        "Frame Debugger 会暂停 Play（默认采完 disable 并恢复）。" +
        "strictBasics 默认 true：Batches/Draw/SetPass 全 0 则 overall ok=false。" +
        "细查请用 unity_profiler_get_counters / unity_frame_debugger_get_event / unity_profiler_get_hierarchy_children。" +
        "fdEventLimit 默认 0：不返回 topEvents。")]
    public async Task<string> UnityPerfCaptureFrameAsync(
        [Description("可选 section 数组 JSON，如 [\"basics\",\"profile\",\"frameDebugger\"]；默认三者都采。overdraw 仅占位")]
        string? sectionsJson = null,
        [Description("Hierarchy Top N，默认 30")] int hierarchyTopN = 30,
        [Description("Frame Debugger topEvents 条数：0=不返回 topEvents（默认，仅 eventCount+summaryByType）；>0=按绘制顺序取前 N 条（非耗时 Top，最大 2000）")] int fdEventLimit = 0,
        [Description("true 则保持 FD 暂停，需稍后 disable")] bool leavePaused = false,
        [Description("true 则结束后关闭 Profiler")] bool disableProfilerAfter = false,
        [Description("true 时 Render 关键计数全 0 视为失败（默认 true）")] bool strictBasics = true)
    {
        logger.LogInformation("perf capture frame");
        try
        {
            string[]? sections = null;
            if (!string.IsNullOrWhiteSpace(sectionsJson))
            {
                sections = JsonSerializer.Deserialize<string[]>(sectionsJson);
            }

            var result = await hub.SendRequestAsync(
                "unity.perfCaptureFrame",
                new
                {
                    sections,
                    hierarchyTopN,
                    fdEventLimit,
                    leavePaused,
                    disableProfilerAfter,
                    strictBasics
                },
                timeoutSeconds: 120);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"perf capture 失败：{ex.Message}";
        }
    }
}
