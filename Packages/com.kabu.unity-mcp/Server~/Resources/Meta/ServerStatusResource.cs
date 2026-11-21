using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Resources;

[McpServerResourceType]
public static class ServerStatusResource
{
    [McpServerResource(
        UriTemplate = "unity://server/status",
        Name = "server-status",
        MimeType = "application/json")]
    [Description("MCP 服务自身状态：连了几个 Unity Editor。不读场景、不改工程。")]
    public static string GetStatus(UnityWebSocketHub hub)
    {
        return JsonSerializer.Serialize(new
        {
            name = "kabu-unity-mcp",
            unityConnections = hub.ConnectedCount
        });
    }
}