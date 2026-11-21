using System;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;

namespace KabuUnityMcp.Editor.Handlers.Components
{
    internal static class ComponentHandlers
    {
        public static void Add(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, ComponentOps.Add(
                    parameters?["instanceId"]?.Value<int?>(),
                    parameters?["path"]?.ToString(),
                    parameters?["name"]?.ToString(),
                    parameters?["componentType"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Remove(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, ComponentOps.Remove(
                    parameters?["instanceId"]?.Value<int?>(),
                    parameters?["path"]?.ToString(),
                    parameters?["name"]?.ToString(),
                    parameters?["componentType"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void SetField(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, ComponentOps.SetField(
                    parameters?["instanceId"]?.Value<int?>(),
                    parameters?["path"]?.ToString(),
                    parameters?["name"]?.ToString(),
                    parameters?["componentType"]?.ToString(),
                    parameters?["fieldName"]?.ToString(),
                    parameters?["value"]?.ToString(),
                    parameters?["valueType"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        private static void Reply(string requestId, McpWebSocketClient client, HandlerResult result)
        {
            if (result.Ok)
                _ = client.SendResponseAsync(requestId, result.Payload);
            else
                _ = client.SendErrorAsync(requestId, result.ErrorCode, result.Error);
        }
    }
}
