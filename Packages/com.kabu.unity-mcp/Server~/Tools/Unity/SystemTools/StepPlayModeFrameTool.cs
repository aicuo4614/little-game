using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class StepPlayModeFrameTool(ILogger<StepPlayModeFrameTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_step_play_mode_frame")]
    [Description(
        "Play Mode 单步下一帧，等价工具栏播放/暂停旁的「下一帧」按钮（EditorApplication.Step）。" +
        "须已在 Play Mode；会等到 Time.frameCount 推进后返回；步进后通常处于 Paused。")]
    public async Task<string> UnityStepPlayModeFrameAsync()
    {
        logger.LogInformation("Step play mode frame");
        try
        {
            var result = await hub.SendRequestAsync("unity.stepPlayModeFrame", null, timeoutSeconds: 20);
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"单步下一帧失败：{ex.Message}";
        }
    }
}
