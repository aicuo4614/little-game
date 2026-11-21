using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using KabuUnityMcp.Server.Models;

namespace KabuUnityMcp.Server.Services;

/// <summary>
/// Unity Editor WebSocket hub：多编辑器连接、按 MCP 客户端隔离的选中目标、串行化发送、请求/通知。
/// </summary>
public sealed class UnityWebSocketHub
{
    private readonly ILogger<UnityWebSocketHub> _logger;
    private readonly ConcurrentDictionary<string, EditorConnection> _connections = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement?>> _pending = new();
    /// <summary>MCP 客户端 key → 选中的 Unity connectionId。</summary>
    private readonly ConcurrentDictionary<string, string> _selectedByClient = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EditorConnection> _pendingOwners = new();
    private readonly object _toolsGate = new();
    private HashSet<string>? _enabledTools;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const int PingIntervalSeconds = 15;
    private const int DefaultTimeoutSeconds = 30;

    public UnityWebSocketHub(ILogger<UnityWebSocketHub> logger)
    {
        _logger = logger;
    }

    public int ConnectedCount => _connections.Count;

    public IReadOnlyCollection<string>? GetEnabledToolNames()
    {
        lock (_toolsGate)
            return _enabledTools == null ? null : _enabledTools.ToArray();
    }

    public bool IsToolEnabled(string toolName)
    {
        lock (_toolsGate)
        {
            if (_enabledTools == null)
                return true;
            return _enabledTools.Contains(toolName);
        }
    }

    public void SetEnabledTools(IEnumerable<string>? toolNames)
    {
        string summary;
        lock (_toolsGate)
        {
            if (toolNames == null)
            {
                _enabledTools = null;
                summary = "(all)";
            }
            else
            {
                _enabledTools = new HashSet<string>(toolNames, StringComparer.OrdinalIgnoreCase);
                summary = string.Join(", ", _enabledTools);
            }
        }

        _logger.LogInformation("Enabled tools updated: {Tools}", summary);
    }

    public IReadOnlyList<UnityEditorInfo> ListEditors()
    {
        var selected = GetSelectedConnectionIdForCurrentClient();

        var list = new List<UnityEditorInfo>();
        foreach (var kv in _connections)
        {
            if (kv.Value.Socket.State != WebSocketState.Open)
                continue;
            var info = kv.Value.Info;
            list.Add(new UnityEditorInfo
            {
                ConnectionId = kv.Key,
                ProjectName = info.ProjectName,
                UnityVersion = info.UnityVersion,
                MachineName = info.MachineName,
                SceneName = info.SceneName,
                ScenePath = info.ScenePath,
                DataPath = info.DataPath,
                ProcessId = info.ProcessId,
                IsPlaying = info.IsPlaying,
                ConnectedAt = info.ConnectedAt,
                IsSelected = string.Equals(kv.Key, selected, StringComparison.OrdinalIgnoreCase)
                             || (selected == null && list.Count == 0)
            });
        }

        // 当前客户端未显式选择且仅一个连接时视为默认选中
        if (selected == null && list.Count == 1)
            list[0].IsSelected = true;

        return list;
    }

    public bool SelectEditor(string connectionId, out string error)
    {
        error = null!;
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            error = "请提供 connectionId。";
            return false;
        }

        connectionId = connectionId.Trim();
        if (!_connections.TryGetValue(connectionId, out var conn) ||
            conn.Socket.State != WebSocketState.Open)
        {
            error = $"未找到已连接的编辑器：{connectionId}。请先 unity_list_editors。";
            return false;
        }

        var clientKey = McpClientScope.Current;
        _selectedByClient[clientKey] = connectionId;
        _logger.LogInformation(
            "Client {ClientKey} selected editor {ConnectionId}",
            clientKey, connectionId);
        return true;
    }

    /// <summary>会话结束时清理该 MCP 客户端的选中状态（可选调用）。</summary>
    public void ClearClientSelection(string? clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
            return;
        _selectedByClient.TryRemove(clientKey.Trim(), out _);
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket connection required");
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var identity = context.Request.Query["editorId"].ToString();
        var connectionId = Guid.TryParseExact(identity, "N", out _) ? identity : Guid.NewGuid().ToString("N");
        var conn = new EditorConnection(webSocket)
        {
            Info =
            {
                ConnectedAt = DateTimeOffset.UtcNow
            }
        };
        if (_connections.TryGetValue(connectionId, out var previous)) previous.Socket.Abort();
        _connections[connectionId] = conn;
        _logger.LogInformation("Unity Editor connected: {ConnectionId} (total {Count})", connectionId, _connections.Count);

        using var pingCts = new CancellationTokenSource();
        var pingTask = KeepAliveAsync(conn, connectionId, pingCts.Token);

        try
        {
            await ReceiveLoopAsync(webSocket, connectionId);
        }
        catch (WebSocketException ex) { _logger.LogDebug(ex, "Unity transport closed: {ConnectionId}", connectionId); }
        finally
        {
            foreach (var entry in _pendingOwners.Where(p => ReferenceEquals(p.Value, conn)))
            {
                _pendingOwners.TryRemove(entry.Key, out _);
                if (_pending.TryRemove(entry.Key, out var pending))
                    pending.TrySetException(new IOException("Unity disconnected before responding. Outcome is unknown; operation was not replayed."));
            }
            pingCts.Cancel();
            try { await pingTask; } catch { /* ignore */ }

            ((ICollection<KeyValuePair<string, EditorConnection>>)_connections).Remove(new KeyValuePair<string, EditorConnection>(connectionId, conn));
            _logger.LogInformation("Unity Editor disconnected: {ConnectionId} (total {Count})", connectionId, _connections.Count);
        }
    }

    public async Task SendNotificationAsync(string method, object? parameters)
    {
        var conn = GetPrimaryConnection();
        var notification = new JsonRpcNotification { Method = method, Params = parameters };
        await conn.SendJsonAsync(notification, _json, CancellationToken.None);
        _logger.LogInformation("Sent notification to Unity: {Method}", method);
    }

    public async Task<JsonElement?> SendRequestAsync(string method, object? parameters, int timeoutSeconds = DefaultTimeoutSeconds)
    {
        var conn = GetPrimaryConnection();
        if (conn.EditorState != "ready") throw new InvalidOperationException("Unity is compiling or registering. Operation was not sent; retry when ready.");
        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;
        _pendingOwners[requestId] = conn;

        var request = new JsonRpcRequest
        {
            Id = requestId,
            Method = method,
            Params = parameters
        };

        try
        {
            await conn.SendJsonAsync(request, _json, CancellationToken.None);
            _logger.LogInformation(
                "Sent request {Id} to Unity: {Method} (client {ClientKey})",
                requestId, method, McpClientScope.Current);
        }
        catch
        {
            _pendingOwners.TryRemove(requestId, out _);
            _pending.TryRemove(requestId, out _);
            throw;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        await using var reg = cts.Token.Register(() =>
        {
            _pendingOwners.TryRemove(requestId, out _);
            if (_pending.TryRemove(requestId, out var pending))
                pending.TrySetException(new TimeoutException(
                    $"等待 Unity 响应超时（{timeoutSeconds}s）。请确认编辑器已连接且窗口未最小化卡死。"));
        });

        return await tcs.Task;
    }

    private string? GetSelectedConnectionIdForCurrentClient()
    {
        if (_selectedByClient.TryGetValue(McpClientScope.Current, out var selected))
            return selected;
        return null;
    }

    private EditorConnection GetPrimaryConnection()
    {
        var selected = GetSelectedConnectionIdForCurrentClient();

        if (!string.IsNullOrEmpty(selected) &&
            _connections.TryGetValue(selected, out var selectedConn) &&
            selectedConn.Socket.State == WebSocketState.Open)
        {
            return selectedConn;
        }

        if (!string.IsNullOrEmpty(selected))
            throw new InvalidOperationException("Selected Unity Editor is offline. Waiting for it to reconnect.");
        var available = _connections.Values.Where(c => c.Socket.State == WebSocketState.Open && c.EditorState == "ready").ToArray();
        if (available.Length == 1) return available[0];
        if (available.Length > 1) throw new InvalidOperationException("Multiple Unity Editors are connected. Select a target first.");

        throw new InvalidOperationException(
            "没有已连接的 Unity Editor。请在 kabu-unity-mcp → Server Window 中点「连接」。");
    }

    private async Task KeepAliveAsync(EditorConnection conn, string connectionId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && conn.Socket.State == WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(PingIntervalSeconds), ct);
                if (conn.Socket.State != WebSocketState.Open) break;
                if (DateTimeOffset.UtcNow - conn.LastPong > TimeSpan.FromSeconds(60))
                {
                    conn.Socket.Abort();
                    break;
                }

                await conn.SendJsonAsync(new JsonRpcNotification
                {
                    Method = "unity.ping",
                    Params = new { timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                }, _json, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // normal
        }
        catch (Exception ex)
        {
            conn.Socket.Abort();
            _logger.LogWarning(ex, "Keep-alive ended for {ConnectionId}", connectionId);
        }
    }

    private async Task ReceiveLoopAsync(WebSocket webSocket, string connectionId)
    {
        var buffer = new byte[4 * 1024];
        var builder = new StringBuilder();

        while (webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
                continue;

            var message = builder.ToString();
            builder.Clear();
            await HandleMessageAsync(webSocket, connectionId, message);
        }
    }

    private async Task HandleMessageAsync(WebSocket webSocket, string connectionId, string message)
    {
        try
        {
            if (!_connections.TryGetValue(connectionId, out var current) || !ReferenceEquals(current.Socket, webSocket)) return;
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            var hasMethod = root.TryGetProperty("method", out var methodEl);
            var method = hasMethod ? methodEl.GetString() : null;
            var hasId = root.TryGetProperty("id", out var idEl);

            if (hasId && !hasMethod)
            {
                var id = idEl.ToString();
                _pendingOwners.TryRemove(id, out _);
                if (_pending.TryRemove(id, out var tcs))
                {
                    if (root.TryGetProperty("error", out var err))
                        tcs.TrySetException(new InvalidOperationException(err.ToString()));
                    else if (root.TryGetProperty("result", out var result))
                        tcs.TrySetResult(result.Clone());
                    else
                        tcs.TrySetResult(null);
                }
                return;
            }

            if (string.Equals(method, "unity.pong", StringComparison.OrdinalIgnoreCase))
            {
                if (_connections.TryGetValue(connectionId, out var alive)) alive.LastPong = DateTimeOffset.UtcNow;
                _logger.LogDebug("Received unity.pong from {ConnectionId}", connectionId);
                return;
            }

            if (method == "unity.editorState" && _connections.TryGetValue(connectionId, out var editor))
            {
                editor.EditorState = root.GetProperty("params").GetProperty("state").GetString() ?? "ready";
                return;
            }
            if (string.Equals(method, "unity.register", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "unity.setToolPreferences", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(method, "unity.register", StringComparison.OrdinalIgnoreCase))
                    ApplyEditorInfoFromParams(connectionId, root);
                ApplyToolPreferencesFromParams(root);
                if (method == "unity.register" && _connections.TryGetValue(connectionId, out var registered))
                {
                    registered.EditorState = root.TryGetProperty("params", out var registration) && registration.TryGetProperty("isCompiling", out var compiling) && compiling.ValueKind == JsonValueKind.True ? "compiling" : "ready";
                    await registered.SendJsonAsync(new JsonRpcNotification { Method = "unity.registered" }, _json, CancellationToken.None);
                }
                _logger.LogInformation("Unity {Method} from {ConnectionId}", method, connectionId);
                return;
            }

            _logger.LogInformation("WS from {ConnectionId}: {Method}", connectionId, method ?? "(response?)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid WS message from {ConnectionId}", connectionId);
        }

        await Task.CompletedTask;
    }

    private void ApplyEditorInfoFromParams(string connectionId, JsonElement root)
    {
        if (!_connections.TryGetValue(connectionId, out var conn))
            return;
        if (!root.TryGetProperty("params", out var p))
            return;

        var info = conn.Info;
        if (p.TryGetProperty("projectName", out var project))
            info.ProjectName = project.GetString();
        if (p.TryGetProperty("unityVersion", out var ver))
            info.UnityVersion = ver.GetString();
        if (p.TryGetProperty("machineName", out var machine))
            info.MachineName = machine.GetString();
        if (p.TryGetProperty("sceneName", out var sceneName))
            info.SceneName = sceneName.GetString();
        if (p.TryGetProperty("scenePath", out var scenePath))
            info.ScenePath = scenePath.GetString();
        if (p.TryGetProperty("dataPath", out var dataPath))
            info.DataPath = dataPath.GetString();
        if (p.TryGetProperty("processId", out var pid) && pid.TryGetInt32(out var processId))
            info.ProcessId = processId;
        if (p.TryGetProperty("isPlaying", out var playing) &&
            (playing.ValueKind == JsonValueKind.True || playing.ValueKind == JsonValueKind.False))
            info.IsPlaying = playing.GetBoolean();
    }

    private void ApplyToolPreferencesFromParams(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var paramsEl))
            return;

        if (!paramsEl.TryGetProperty("enabledTools", out var toolsEl) || toolsEl.ValueKind != JsonValueKind.Array)
            return;

        var names = new List<string>();
        foreach (var item in toolsEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var name = item.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name!);
            }
        }

        SetEnabledTools(names);
    }

    private sealed class EditorConnection
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public EditorConnection(WebSocket socket)
        {
            Socket = socket;
            Info = new EditorInfoState();
        }

        public WebSocket Socket { get; }
        public EditorInfoState Info { get; }
        public DateTimeOffset LastPong { get; set; } = DateTimeOffset.UtcNow;
        public string EditorState { get; set; } = "registering";

        public async Task SendJsonAsync(object payload, JsonSerializerOptions json, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, json));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            ct = timeout.Token;
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Socket.State != WebSocketState.Open)
                    throw new InvalidOperationException("Unity WebSocket 已断开。");
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }

    private sealed class EditorInfoState
    {
        public string? ProjectName { get; set; }
        public string? UnityVersion { get; set; }
        public string? MachineName { get; set; }
        public string? SceneName { get; set; }
        public string? ScenePath { get; set; }
        public string? DataPath { get; set; }
        public int ProcessId { get; set; }
        public bool IsPlaying { get; set; }
        public DateTimeOffset ConnectedAt { get; set; }
    }
}

public sealed class UnityEditorInfo
{
    public string ConnectionId { get; set; } = "";
    public string? ProjectName { get; set; }
    public string? UnityVersion { get; set; }
    public string? MachineName { get; set; }
    public string? SceneName { get; set; }
    public string? ScenePath { get; set; }
    public string? DataPath { get; set; }
    public int ProcessId { get; set; }
    public bool IsPlaying { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public bool IsSelected { get; set; }
}
