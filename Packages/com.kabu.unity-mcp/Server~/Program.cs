using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("UNITY_MCP_ASPPORT") ?? "3827";
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[HH:mm:ss.fff] ";
    options.SingleLine = true;
});

var logPath = Environment.GetEnvironmentVariable("UNITY_MCP_LOG_PATH");
if (!string.IsNullOrWhiteSpace(logPath)) builder.Logging.AddProvider(new ServerFileLoggerProvider(logPath));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<UnityWebSocketHub>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "kabu-unity-mcp",
            Version = "0.2.0"
        };
    })
    .WithHttpTransport(options =>
    {
        // 仍支持 2026-07-28 无 session 客户端；initialize 握手客户端拿到 SessionId，便于隔离 selectedEditor。
        options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
        // Cursor 重连 GET 会带 Last-Event-ID；没有事件存储时 SDK 回 400，客户端就放弃。
#pragma warning disable MCPEXP002 // RunSessionHandler 是当前唯一能在 session 存活期间发通知的入口
        options.RunSessionHandler = static async (_, mcpServer, cancellationToken) =>
        {
            if (string.IsNullOrEmpty(mcpServer.SessionId))
            {
                await mcpServer.RunAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            using var keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var keepalive = CursorSseKeepAlive.RunAsync(mcpServer, keepaliveCts.Token);
            try
            {
                await mcpServer.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                keepaliveCts.Cancel();
                try
                {
                    await keepalive.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // keepalive 被取消
                }
            }
        };
#pragma warning restore MCPEXP002
    })
    .WithDistributedCacheEventStreamStore()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithRequestFilters(filters =>
    {
        filters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            using var _ = McpClientScope.Begin(McpClientKeyResolver.Resolve(context));
            var result = await next(context, cancellationToken);
            var hub = context.Services?.GetService<UnityWebSocketHub>();
            if (hub == null || result.Tools == null)
                return result;

            var enabled = hub.GetEnabledToolNames();
            if (enabled == null)
                return result;

            var allowed = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
            result = new ListToolsResult
            {
                Tools = result.Tools.Where(t => allowed.Contains(t.Name)).ToList(),
                NextCursor = result.NextCursor
            };
            return result;
        });

        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            using var _ = McpClientScope.Begin(McpClientKeyResolver.Resolve(context));
            var hub = context.Services?.GetService<UnityWebSocketHub>();
            var toolName = context.Params?.Name;
            if (hub != null && !string.IsNullOrEmpty(toolName) && !hub.IsToolEnabled(toolName))
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content =
                    [
                        new TextContentBlock { Text = $"工具 '{toolName}' 已在 Unity 工程 settings.json 中关闭。" }
                    ]
                };
            }

            return await next(context, cancellationToken);
        });
    });

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCors();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.MapMcp("/mcp");
app.MapHealthChecks("/health");

app.Map("/ws", async context =>
{
    var hub = context.RequestServices.GetRequiredService<UnityWebSocketHub>();
    await hub.HandleAsync(context);
});

app.MapGet("/", (UnityWebSocketHub hub) => new
{
    name = "kabu-unity-mcp",
    version = "0.1.0",
    transport = "streamable-http + websocket",
    endpoints = new
    {
        mcp = "/mcp",
        websocket = "/ws",
        health = "/health"
    },
    unityConnections = hub.ConnectedCount,
    enabledTools = hub.GetEnabledToolNames() ?? (object)"(all until Unity registers)",
    status = "running"
});

await app.RunAsync();
