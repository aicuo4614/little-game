using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class SelectEditorTool(ILogger<SelectEditorTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_select_editor")]
    [Description("选择后续 unity_* 工具请求发往的目标 Unity Editor（connectionId 来自 unity_list_editors）。选中状态按 MCP 客户端隔离，不影响其他客户端。")]
    public string UnitySelectEditor(
        [Description("编辑器连接 ID（unity_list_editors 返回的 ConnectionId）")] string connectionId)
    {
        logger.LogInformation("Select editor {Id} (client {Client})", connectionId, McpClientScope.Current);
        if (!hub.SelectEditor(connectionId, out var error))
            return error;
        return $"已选择编辑器：{connectionId.Trim()}（本客户端）。后续本客户端的工具请求将发往该连接。";
    }
}
