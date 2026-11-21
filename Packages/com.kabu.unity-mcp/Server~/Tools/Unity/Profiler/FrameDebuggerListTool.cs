using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class FrameDebuggerListTool(ILogger<FrameDebuggerListTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_frame_debugger_list")]
    [Description(
        "列出当前帧 Frame Debugger 事件目录（瘦字段：index/type/name/gameObject/path）。" +
        "不含 shader/pass/贴图等详情——那些用 unity_frame_debugger_get_event。" +
        "支持 offset、limit（默认 50，最大 200）、eventType、nameContains 过滤。" +
        "须先 enable。")]
    public async Task<string> UnityFrameDebuggerListAsync(
        [Description("起始偏移，默认 0")] int offset = 0,
        [Description("返回条数，默认 50，最大 200")] int limit = 50,
        [Description("按事件类型子串过滤，如 Mesh / InstancedMesh / Clear")] string? eventType = null,
        [Description("按事件名/Pass 路径子串过滤")] string? nameContains = null)
    {
        logger.LogInformation("Frame debugger list offset={Offset} limit={Limit}", offset, limit);
        try
        {
            var result = await hub.SendRequestAsync("unity.frameDebuggerList", new
            {
                offset,
                limit,
                eventType = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim(),
                nameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains.Trim()
            });
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"列出 Frame Debugger 事件失败：{ex.Message}";
        }
    }
}
