using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Console;

[McpServerToolType]
public class GetConsoleLogsTool(ILogger<GetConsoleLogsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_console_logs")]
    [Description("读取 Unity Editor Console 当前条目，包含已经显示的编译/导入警告。可按类型过滤。Console 条目没有时间戳，lastMinutes 不会丢掉已有警告。")]
    [return: Description("日志文本")]
    public async Task<string> UnityGetConsoleLogsAsync(
        [Description("最多条数，默认 100")] int maxEntries = 100,
        [Description("过滤类型：Log / Warning / Error / Assert / Exception；空=全部")] string? logType = null,
        [Description("是否包含堆栈，默认 false")] bool includeStackTrace = false,
        [Description("只取最近 N 分钟；0=全部")] int lastMinutes = 0)
    {
        logger.LogInformation("Get console logs");
        try
        {
            var result = await hub.SendRequestAsync("unity.getConsoleLogs", new
            {
                maxEntries,
                logType = string.IsNullOrWhiteSpace(logType) ? null : logType.Trim(),
                includeStackTrace,
                lastMinutes
            });
            if (result == null)
                return "Unity 未返回数据。";

            var el = result.Value;
            if (!el.TryGetProperty("logs", out var logs) || logs.ValueKind != JsonValueKind.Array)
                return el.ToString();

            var sb = new StringBuilder();
            var count = el.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
            sb.AppendLine($"Console 日志 {count} 条：");
            foreach (var log in logs.EnumerateArray())
            {
                var type = log.TryGetProperty("type", out var ty) ? ty.GetString() : "?";
                var message = log.TryGetProperty("message", out var m) ? m.GetString() : "";
                sb.AppendLine($"[{type}] {message}");
                if (includeStackTrace && log.TryGetProperty("stackTrace", out var st) &&
                    st.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(st.GetString()))
                    sb.AppendLine(st.GetString());
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"读取日志失败：{ex.Message}";
        }
    }
}
