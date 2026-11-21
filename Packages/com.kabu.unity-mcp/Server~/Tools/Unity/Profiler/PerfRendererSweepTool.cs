using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class PerfRendererSweepTool(ILogger<PerfRendererSweepTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_perf_renderer_sweep")]
    [Description(
        "按组临时 SetActive(false) 扫描 Renderer 对 Batches 的贡献（schema=kabu.perf.renderer-sweep/v1）。" +
        "流程：未 Play 则进 Play → 采 baseline → 最多 batchCount 批（关→等稳→采→恢复→等稳）→ 按 batchesDrop 降序返回 topN。" +
        "采样须连续 stableReads 次 Batches/Draw/SetPass 一致才采纳；开关物体多时自动加长等待，避免脏帧虚高 drop。" +
        "groupBy: sibling|hierarchyChunk|parent；target: rendererGo|parent。默认 restore=true。可能耗时数分钟。")]
    public async Task<string> UnityPerfRendererSweepAsync(
        [Description("可选根路径，只扫该子树；空=整场景")] string? rootPath = null,
        [Description("分组：sibling | hierarchyChunk | parent，默认 sibling")] string groupBy = "sibling",
        [Description("开关目标：rendererGo | parent，默认 rendererGo")] string target = "rendererGo",
        [Description("最多批次数（循环上限），默认 10，最大 50")] int batchCount = 10,
        [Description("返回 Top N（按 batchesDrop），默认 50")] int topN = 50,
        [Description("基础等待帧数（还会按开关物体数加长），默认 16")] int settleFrames = 16,
        [Description("连续一致采样次数才采纳，默认 3，范围 2～5")] int stableReads = 3,
        [Description("是否包含未激活 Renderer，默认 false")] bool includeInactive = false,
        [Description("结束后确保恢复 activeSelf，默认 true")] bool restore = true,
        [Description("若本工具自动进 Play，结束时是否退出，默认 false")] bool exitPlayWhenDone = false)
    {
        logger.LogInformation("perf renderer sweep root={Root} groupBy={GroupBy} batches={BatchCount}",
            rootPath, groupBy, batchCount);
        try
        {
            var result = await hub.SendRequestAsync(
                "unity.perfRendererSweep",
                new
                {
                    rootPath,
                    groupBy,
                    target,
                    batchCount,
                    topN,
                    settleFrames,
                    stableReads,
                    includeInactive,
                    restore,
                    exitPlayWhenDone
                },
                timeoutSeconds: 900);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"renderer sweep 失败：{ex.Message}";
        }
    }
}
