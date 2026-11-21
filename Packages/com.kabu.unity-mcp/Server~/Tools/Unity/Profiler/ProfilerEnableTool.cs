using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class ProfilerEnableTool(ILogger<ProfilerEnableTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_profiler_enable")]
    [Description(
        "启用 Unity Profiler。" +
        "副作用：显著增加编辑器/Play 开销；可选写入 .raw、开启 allocation callstacks、按 areas 开关 ProfilerArea。" +
        "建议 Play Mode 下使用。用完请 unity_profiler_disable。")]
    public async Task<string> UnityProfilerEnableAsync(
        [Description("可选 .raw 录制路径")] string? logFile = null,
        [Description("是否开启 allocation callstacks")] bool enableCallstacks = false,
        [Description("可选 JSON 对象字符串，如 {\"CPU\":true,\"GPU\":true,\"Memory\":true}")] string? areasJson = null)
    {
        logger.LogInformation("Profiler enable");
        try
        {
            object? areas = null;
            if (!string.IsNullOrWhiteSpace(areasJson))
            {
                areas = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(areasJson);
            }

            var result = await hub.SendRequestAsync("unity.profilerEnable", new
            {
                logFile = string.IsNullOrWhiteSpace(logFile) ? null : logFile.Trim(),
                enableCallstacks,
                areas
            });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"启用 Profiler 失败：{ex.Message}";
        }
    }
}
