using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class BringEditorToForegroundTool(ILogger<BringEditorToForegroundTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_bring_editor_to_foreground")]
    [Description("尝试将目标 Unity Editor 窗口置于前台（Windows 下会 Restore + SetForeground；其他平台 Focus EditorWindow）。需要 Unity 已连接。")]
    public async Task<string> UnityBringEditorToForegroundAsync()
    {
        logger.LogInformation("Bring editor to foreground");
        try
        {
            var result = await hub.SendRequestAsync("unity.bringToForeground", null);
            if (result?.TryGetProperty("message", out var msg) == true)
                return msg.GetString() ?? result.Value.ToString();
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex)
        {
            return $"置于前台失败：{ex.Message}";
        }
    }
}
