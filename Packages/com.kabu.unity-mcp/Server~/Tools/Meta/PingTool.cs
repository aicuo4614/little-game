using System.ComponentModel;
using ModelContextProtocol.Server;

namespace KabuUnityMcp.Server.Tools.Meta;

/// <summary>
/// 不操控 Unity：仅确认 MCP HTTP 可达。
/// </summary>
[McpServerToolType]
public static class PingTool
{
    [McpServerTool(Name = "unity_ping")]
    [Description("健康检查：确认 MCP HTTP 是否通。可选 message 会回显。不操作 Unity 场景。")]
    [return: Description("确认 MCP 服务可达的固定回包")]
    public static string UnityPing(
        [Description("可选标签，会出现在返回内容里")] string? message = null)
    {
        var label = string.IsNullOrWhiteSpace(message) ? "ok" : message.Trim();
        return $"pong from KabuUnityMcp（{label}）";
    }
}
