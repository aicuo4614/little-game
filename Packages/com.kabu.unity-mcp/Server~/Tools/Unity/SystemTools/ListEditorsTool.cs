using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class ListEditorsTool(ILogger<ListEditorsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_list_editors")]
    [Description("列出当前已通过 WebSocket 连接到 MCP Server 的 Unity Editor，含项目名、版本、场景、是否为当前 MCP 客户端选中的目标。无需发往 Unity。")]
    public string UnityListEditors()
    {
        logger.LogInformation("List editors (client {Client})", McpClientScope.Current);
        var editors = hub.ListEditors();
        if (editors.Count == 0)
            return "当前没有已连接的 Unity Editor。请在 kabu-unity-mcp → Server Window 中点「连接」。";

        var sb = new StringBuilder();
        sb.AppendLine($"已连接 {editors.Count} 个编辑器（[选中] 仅对本 MCP 客户端生效）：");
        foreach (var e in editors)
        {
            var mark = e.IsSelected ? " [选中]" : "";
            sb.AppendLine(
                $"- {e.ConnectionId}{mark}: {e.ProjectName ?? "(未知项目)"} / Unity {e.UnityVersion ?? "?"} / {e.MachineName ?? "?"}");
            if (!string.IsNullOrEmpty(e.ScenePath) || !string.IsNullOrEmpty(e.SceneName))
                sb.AppendLine($"  场景: {e.SceneName} ({e.ScenePath})");
            if (e.ProcessId > 0)
                sb.AppendLine($"  PID: {e.ProcessId}, Playing: {e.IsPlaying}");
        }

        return sb.ToString().TrimEnd();
    }
}
