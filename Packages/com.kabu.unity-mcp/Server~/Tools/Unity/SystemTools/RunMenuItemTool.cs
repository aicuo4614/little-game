using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class RunMenuItemTool(ILogger<RunMenuItemTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_run_menu_item")]
    [Description("执行 Unity Editor 菜单项。menuPath 如 Assets/Refresh、Edit/Undo、Window/General/Console。")]
    public async Task<string> UnityRunMenuItemAsync(
        [Description("菜单完整路径")] string menuPath)
    {
        if (string.IsNullOrWhiteSpace(menuPath))
            return "请提供 menuPath。";
        logger.LogInformation("Run menu {Path}", menuPath);
        try
        {
            var result = await hub.SendRequestAsync("unity.runMenuItem", new { menuPath = menuPath.Trim() });
            if (result?.TryGetProperty("message", out var msg) == true)
                return msg.GetString() ?? "已执行。";
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex) { return $"执行菜单失败：{ex.Message}"; }
    }
}
