using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Meta;

/// <summary>
/// 不改场景：只查询 WebSocket 桥连接数。
/// </summary>
[McpServerToolType]
public class WsStatusTool(UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_ws_status")]
    [Description("查询当前有多少个 Unity Editor 已通过 WebSocket 连接到 /ws。")]
    [return: Description("连接数量说明；为 0 时提示用户在 Unity 里连接")]
    public string UnityWsStatus()
    {
        var count = hub.ConnectedCount;
        return count == 0
            ? "当前没有 Unity Editor 连接 /ws。请在 Unity 菜单 kabu-unity-mcp → Server Window 中先「启动服务器」再点「连接」。"
            : $"当前有 {count} 个 Unity Editor 已连接 /ws。";
    }
}
