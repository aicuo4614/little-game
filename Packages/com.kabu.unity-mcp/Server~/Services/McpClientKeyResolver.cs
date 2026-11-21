using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KabuUnityMcp.Server.Services;

/// <summary>
/// 从 MCP 请求解析客户端隔离 key：优先 SessionId，其次 HTTP 连接 id。
/// </summary>
public static class McpClientKeyResolver
{
    public const string SessionIdHeader = "Mcp-Session-Id";

    public static string Resolve(RequestContext<ListToolsRequestParams> context) =>
        Resolve(context.Server?.SessionId, context.Services?.GetService<IHttpContextAccessor>()?.HttpContext);

    public static string Resolve(RequestContext<CallToolRequestParams> context) =>
        Resolve(context.Server?.SessionId, context.Services?.GetService<IHttpContextAccessor>()?.HttpContext);

    public static string Resolve(string? sessionId, HttpContext? http)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
            return "session:" + sessionId.Trim();

        var header = http?.Request.Headers[SessionIdHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header))
            return "session:" + header.Trim();

        // Stateless / 无 session：尽量用 TCP 连接 id（keep-alive 下同一客户端较稳定）。
        var connId = http?.Connection.Id;
        if (!string.IsNullOrWhiteSpace(connId))
            return "conn:" + connId;

        return McpClientScope.DefaultKey;
    }
}
