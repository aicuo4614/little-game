using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KabuUnityMcp.Networking
{
    /// <summary>
    /// JSON-RPC WebSocket client for MCP server /ws.
    /// Call <see cref="ProcessMainThreadQueue"/> from EditorApplication.update.
    /// </summary>
    public sealed class McpWebSocketClient : IDisposable
    {
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private sealed class Connection
        {
            internal readonly ClientWebSocket Socket = new ClientWebSocket();
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal readonly SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
            internal long LastReceived = DateTime.UtcNow.Ticks;
            internal int Stopped;
            internal readonly TaskCompletionSource<bool> Registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private Connection _connection;
        private readonly string _url;
        private volatile bool _connecting;
        private volatile bool _connected;
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<string> OnError;
        public event Action<string, JToken> OnNotification;
        public event Action<string, string, JToken> OnRequest;
        public bool IsConnected => _connected && _connection != null && _connection.Socket.State == WebSocketState.Open;
        public bool IsConnecting => _connecting;
        public string Url => _url;
        public McpWebSocketClient(string url = null) { _url = string.IsNullOrEmpty(url) ? McpEndpoints.WsUrl : url; }

        public async Task<bool> ConnectAsync()
        {
            if (_connecting || IsConnected) return IsConnected;
            var old = _connection;
            if (old != null) Stop(old, "replaced");
            var connection = new Connection();
            _connection = connection;
            _connecting = true;
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Cancellation.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await connection.Socket.ConnectAsync(new Uri(_url), timeout.Token);
                }
                if (!ReferenceEquals(_connection, connection) || connection.Stopped != 0) return false;
                _connected = true;
                EnqueueCurrent(connection, () => { if (connection.Stopped == 0) OnConnected?.Invoke(); });
                _ = Task.Run(() => ReceiveLoopAsync(connection));
                _ = Task.Run(() => WatchdogAsync(connection));
                return true;
            }
            catch (Exception ex)
            {
                EnqueueCurrent(connection, () => OnError?.Invoke("Connection failed: " + ex.Message));
                Stop(connection, "Connection failed: " + ex.Message);
                return false;
            }
            finally
            {
                if (ReferenceEquals(_connection, connection)) _connecting = false;
            }
        }

        public Task DisconnectAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }

        private void Stop(Connection connection, string reason)
        {
            if (Interlocked.Exchange(ref connection.Stopped, 1) != 0) return;
            connection.Registered.TrySetCanceled();
            connection.Cancellation.Cancel();
            connection.Socket.Abort();
            connection.Socket.Dispose();
            if (ReferenceEquals(_connection, connection))
            {
                _connected = false;
                _connecting = false;
                EnqueueCurrent(connection, () => OnDisconnected?.Invoke(reason));
            }
        }

        private void EnqueueCurrent(Connection connection, Action action) =>
            Enqueue(() => { if (ReferenceEquals(_connection, connection)) action(); });

        private async Task WatchdogAsync(Connection connection)
        {
            try
            {
                while (!connection.Cancellation.IsCancellationRequested)
                {
                    await Task.Delay(5000, connection.Cancellation.Token);
                    if (DateTime.UtcNow.Ticks - Interlocked.Read(ref connection.LastReceived) > TimeSpan.FromSeconds(60).Ticks)
                    {
                        Stop(connection, "Server heartbeat timed out");
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        public async Task WaitForRegistrationAsync()
        {
            var connection = _connection;
            if (connection == null) throw new InvalidOperationException("Not connected");
            if (await Task.WhenAny(connection.Registered.Task, Task.Delay(10000)) != connection.Registered.Task)
                throw new TimeoutException("Unity registration acknowledgement timed out");
            await connection.Registered.Task;
        }

        public async Task SendNotificationAsync(string method, object parameters = null)
        {
            if (!IsConnected)
                throw new InvalidOperationException("WebSocket is not connected.");

            var payload = new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters
            };
            await SendRawAsync(JsonConvert.SerializeObject(payload));
        }

        public async Task SendResponseAsync(string requestId, object result)
        {
            if (!IsConnected)
                throw new InvalidOperationException("WebSocket is not connected.");

            var payload = new
            {
                jsonrpc = "2.0",
                id = requestId,
                result
            };
            await SendRawAsync(JsonConvert.SerializeObject(payload));
        }

        public async Task SendErrorAsync(string requestId, int code, string message)
        {
            if (!IsConnected)
                return;

            var payload = new
            {
                jsonrpc = "2.0",
                id = requestId,
                error = new { code, message }
            };
            await SendRawAsync(JsonConvert.SerializeObject(payload));
        }

        public void ProcessMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action?.Invoke(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        public void PumpMainThread() => ProcessMainThreadQueue();

        public void MarkClosing() { }

        public void Dispose()
        {
            var connection = _connection;
            if (connection != null) Stop(connection, "client disconnect");
        }

        private Task SendRawAsync(string json) => SendRawAsync(_connection, json);

        private async Task SendRawAsync(Connection connection, string json)
        {
            if (connection == null) throw new InvalidOperationException("WebSocket is not connected.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Cancellation.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var entered = false;
                try
                {
                    await connection.SendLock.WaitAsync(timeout.Token);
                    entered = true;
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await connection.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token);
                }
                catch
                {
                    Stop(connection, "Send failed or timed out");
                    throw;
                }
                finally { if (entered) connection.SendLock.Release(); }
            }
        }

        private async Task ReceiveLoopAsync(Connection connection)
        {
            var buffer = new byte[4096];
            var message = new System.IO.MemoryStream();
            var reason = "server closed";
            try
            {
                while (!connection.Cancellation.IsCancellationRequested)
                {
                    var result = await connection.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), connection.Cancellation.Token);
                    Interlocked.Exchange(ref connection.LastReceived, DateTime.UtcNow.Ticks);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.MessageType != WebSocketMessageType.Text) continue;
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;
                    HandleMessage(connection, Encoding.UTF8.GetString(message.ToArray()));
                    message.SetLength(0);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { reason = "Receive failed: " + ex.Message; }
            finally { message.Dispose(); Stop(connection, reason); }
        }

        private void HandleMessage(Connection connection, string message)
        {
            try
            {
                var root = JObject.Parse(message);
                var method = root["method"]?.ToString();
                if (string.IsNullOrEmpty(method))
                    return;

                if (method == "unity.registered") { connection.Registered.TrySetResult(true); return; }
                if (method == "unity.ping")
                {
                    _ = ReplyPongAsync(connection);
                    return;
                }

                var parameters = root["params"];
                var idToken = root["id"];
                if (idToken != null && idToken.Type != JTokenType.Null)
                {
                    var requestId = idToken.Type == JTokenType.String
                        ? idToken.Value<string>()
                        : idToken.ToString();
                    EnqueueCurrent(connection, () => { if (connection.Stopped == 0) OnRequest?.Invoke(requestId, method, parameters); });
                }
                else
                {
                    EnqueueCurrent(connection, () => OnNotification?.Invoke(method, parameters));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[kabu-unity-mcp] Bad message: {ex.Message}");
            }
        }

        private async Task ReplyPongAsync(Connection connection)
        {
            try { await SendRawAsync(connection, "{\"jsonrpc\":\"2.0\",\"method\":\"unity.pong\"}"); }
            catch { /* SendRawAsync already stopped this connection. */ }
        }
        private void Enqueue(Action action) => _mainThreadQueue.Enqueue(action);
    }
}
