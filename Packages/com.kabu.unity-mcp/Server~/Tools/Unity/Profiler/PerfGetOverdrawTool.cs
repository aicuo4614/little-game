using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class PerfGetOverdrawTool(ILogger<PerfGetOverdrawTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_perf_get_overdraw")]
    [Description(
        "Play 态采集过绘（schema=kabu.perf.overdraw/v1）。" +
        "未 Play 会自动进 Play；Game 相机 Additive 采整屏层数，再对透明优先候选做开关 A/B。" +
        "data.top[]：sumLayersDrop/avgLayersDrop（关掉该物体后少画的层×像素；主看 sum），scoreKind=play_ab_layers；含 markdownTable。" +
        "编辑器近似，非真机 TBDR。副作用：短暂禁用候选 Renderer.enabled；默认恢复。耗时随 candidateLimit 增加。")]
    public async Task<string> UnityPerfGetOverdrawAsync(
        [Description("返回 TopN（按 sumLayersDrop），默认 20，范围 1～50")] int topN = 20,
        [Description("A/B 候选上限（透明优先预筛），默认 30，范围 1～80")] int candidateLimit = 30,
        [Description("每次开关后等待帧数，默认 12")] int settleFrames = 12,
        [Description("可选：只统计该层级路径子树")] string rootPath = "")
    {
        logger.LogInformation(
            "perf get overdraw topN={TopN} candidates={Cand} settle={Settle} root={Root}",
            topN, candidateLimit, settleFrames, rootPath);
        try
        {
            // A/B 多候选可能较久
            var result = await hub.SendRequestAsync(
                "unity.perfGetOverdraw",
                new
                {
                    topN,
                    candidateLimit,
                    settleFrames,
                    rootPath = string.IsNullOrWhiteSpace(rootPath) ? null : rootPath
                },
                timeoutSeconds: 600);
            if (result == null) return "Unity 未返回数据。";
            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"overdraw 采集失败：{ex.Message}";
        }
    }
}
