using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using KabuUnityMcp.Networking;
using KabuUnityMcp.Server.Services;

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddSingleton<UnityWebSocketHub>();
await using var app = builder.Build();
app.UseWebSockets();
var hub = app.Services.GetRequiredService<UnityWebSocketHub>();
app.Map("/ws", hub.HandleAsync);
app.Map("/silent", async context =>
{
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    try { while (socket.State == System.Net.WebSockets.WebSocketState.Open) await socket.ReceiveAsync(new ArraySegment<byte>(new byte[4096]), context.RequestAborted); }
    catch (System.Net.WebSockets.WebSocketException) { }
});
app.Map("/stall", async context =>
{
    try { await Task.Delay(30000, context.RequestAborted); } catch (OperationCanceledException) { }
});
await app.StartAsync();
var url = app.Urls.Single().Replace("http:", "ws:") + "/ws?editorId=" + Guid.NewGuid().ToString("N");
using var client = new McpWebSocketClient(url);
async Task Register(McpWebSocketClient c)
{
    Check(await c.ConnectAsync(), "transport connected");
    await c.SendNotificationAsync("unity.register", new { projectName = "reliability-test" });
    await c.WaitForRegistrationAsync();
}
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
await Register(client);
var id = hub.ListEditors().Single().ConnectionId;
Check(hub.SelectEditor(id, out _), "select editor");
await Task.WhenAll(Enumerable.Range(0, 100).Select(i => client.SendNotificationAsync("test.concurrent", new { i })));
Check(client.IsConnected, "100 concurrent sends");
await client.SendNotificationAsync("unity.editorState", new { state = "compiling" });
await Task.Delay(100);
try { await hub.SendRequestAsync("test.operation", null); throw new Exception("compiling accepted command"); }
catch (InvalidOperationException) { Console.WriteLine("PASS compilation rejects commands before sending"); }
await client.DisconnectAsync();
await Task.Delay(100);
await Register(client);
Check(hub.ListEditors().Single().ConnectionId == id && hub.ListEditors().Single().IsSelected, "identity and selection survive reconnect");
var pending = hub.SendRequestAsync("test.pending", null);
await Task.Delay(50);
await client.DisconnectAsync();
try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("pending survived disconnect"); }
catch (System.IO.IOException) { Console.WriteLine("PASS disconnect immediately fails pending request"); }
await Register(client);
using var replacement = new McpWebSocketClient(url);
await Register(replacement);
await Task.Delay(100);
Check(hub.ListEditors().Count == 1 && hub.ListEditors().Single().ConnectionId == id, "old socket cleanup preserves replacement");
for (var i = 0; i < 10; i++) { await Register(client); await client.DisconnectAsync(); }
Console.WriteLine("PASS repeated reconnect and cancellation");
async Task CheckClientTimeouts()
{
    using var silent = new McpWebSocketClient(app.Urls.Single().Replace("http:", "ws:") + "/silent");
    Check(await silent.ConnectAsync(), "silent transport connected");
    try { await silent.WaitForRegistrationAsync(); throw new Exception("registration accepted without acknowledgement"); }
    catch (TimeoutException) { Console.WriteLine("PASS registration acknowledgement timeout"); }
    var connection = typeof(McpWebSocketClient).GetField("_connection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(silent);
    connection.GetType().GetField("LastReceived", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(connection, DateTime.UtcNow.AddMinutes(-2).Ticks);
    await Task.Delay(5500);
    Check(!silent.IsConnected, "client heartbeat expires silent connection");
}
async Task CheckServerTimeout()
{
    using var raw = new System.Net.WebSockets.ClientWebSocket();
    await raw.ConnectAsync(new Uri(app.Urls.Single().Replace("http:", "ws:") + "/ws"), default);
    await Task.Delay(100);
    var connections = (System.Collections.IEnumerable)typeof(UnityWebSocketHub).GetField("_connections", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(hub);
    foreach (var entry in connections)
    {
        var connection = entry.GetType().GetProperty("Value").GetValue(entry);
        connection.GetType().GetProperty("LastPong").SetValue(connection, DateTimeOffset.UtcNow.AddMinutes(-2));
    }
    await Task.Delay(16000);
    Check(hub.ConnectedCount == 0, "server heartbeat removes connection without pong");
}
await replacement.DisconnectAsync();
await Task.Delay(100);
using (var stalled = new McpWebSocketClient(app.Urls.Single().Replace("http:", "ws:") + "/stall"))
{
    var connecting = stalled.ConnectAsync();
    await Task.Delay(100);
    await stalled.DisconnectAsync();
    Check(!await connecting.WaitAsync(TimeSpan.FromSeconds(2)), "disconnect cancels in-flight handshake");
}
await Task.WhenAll(CheckClientTimeouts(), CheckServerTimeout());
await app.StopAsync();
namespace UnityEngine { public static class Debug { public static void LogWarning(object o) {} public static void LogException(Exception e) { Console.WriteLine(e); } } }
