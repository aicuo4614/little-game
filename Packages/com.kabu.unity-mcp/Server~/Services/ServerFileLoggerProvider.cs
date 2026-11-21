namespace KabuUnityMcp.Server.Services;

/// <summary>Owned by the external server, independent of Unity domain reloads.</summary>
internal sealed class ServerFileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    public ServerFileLoggerProvider(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
    }
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { lock (_gate) _writer.Dispose(); }
    private sealed class FileLogger(ServerFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            lock (owner._gate)
            {
                owner._writer.WriteLine($"{DateTimeOffset.Now:O} [{level}] {category}: {formatter(state, exception)}");
                if (exception != null) owner._writer.WriteLine(exception);
            }
        }
    }
}
