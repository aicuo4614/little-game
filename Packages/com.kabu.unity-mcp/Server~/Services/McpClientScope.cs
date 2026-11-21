namespace KabuUnityMcp.Server.Services;

/// <summary>
/// 当前 MCP 工具调用所属的客户端作用域（按 session / 连接隔离）。
/// 由 CallToolFilter 设置，Hub 读取 selectedEditor 时使用。
/// </summary>
public static class McpClientScope
{
    private static readonly AsyncLocal<string?> CurrentKey = new();

    public const string DefaultKey = "default";

    public static string Current =>
        string.IsNullOrEmpty(CurrentKey.Value) ? DefaultKey : CurrentKey.Value!;

    public static IDisposable Begin(string? clientKey)
    {
        var previous = CurrentKey.Value;
        CurrentKey.Value = string.IsNullOrWhiteSpace(clientKey) ? DefaultKey : clientKey.Trim();
        return new Pop(previous);
    }

    private sealed class Pop(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            CurrentKey.Value = previous;
        }
    }
}
