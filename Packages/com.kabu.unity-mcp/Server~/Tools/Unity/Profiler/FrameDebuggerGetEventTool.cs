using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class FrameDebuggerGetEventTool(ILogger<FrameDebuggerGetEventTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_frame_debugger_get_event")]
    [Description(
        "按 list 返回的 index 取单条完整事件数据（网格/顶点/实例数、shader、pass、keywords、" +
        "Blend/Depth/Stencil、RT、贴图与 float/vector 绑定、batchBreak）。" +
        "副作用：会移动 Frame Debugger 回放游标并重绘，需等 1+ 编辑器帧。" +
        "模型应先 list 再对可疑的少数几条 get。须先 enable。")]
    public async Task<string> UnityFrameDebuggerGetEventAsync(
        [Description("事件序号（来自 list 的 index）")] int index)
    {
        logger.LogInformation("Frame debugger get event {Index}", index);
        try
        {
            var result = await hub.SendRequestAsync(
                "unity.frameDebuggerGetEvent",
                new { index },
                timeoutSeconds: 60);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"读取 Frame Debugger 事件失败：{ex.Message}";
        }
    }
}
