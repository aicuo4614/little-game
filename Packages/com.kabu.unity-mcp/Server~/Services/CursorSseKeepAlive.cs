using ModelContextProtocol.Server;

namespace KabuUnityMcp.Server.Services;

/// <summary>
/// Cursor 的 HTTP 客户端在 SSE 空闲约 5 分钟后会关掉流，再用同一 session 重开 GET。
/// SDK 对这次重开回 400，Cursor 只重试两次就停在 failed，必须手动重连。
/// 定期往 session 写一条通知，让这条 SSE 一直有数据。
/// </summary>
internal static class CursorSseKeepAlive
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(45);

    public static async Task RunAsync(McpServer server, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendCts.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await server.SendNotificationAsync(
                        "notifications/message",
                        new Dictionary<string, object?>
                        {
                            ["level"] = "debug",
                            ["logger"] = "kabu-unity-mcp",
                            ["data"] = "keepalive",
                        },
                        cancellationToken: sendCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // 这一拍没写出去（例如客户端还没打开 GET）。下一拍再试。
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine($"[kabu-unity-mcp] SSE keepalive skipped: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // session 结束
        }
    }
}
