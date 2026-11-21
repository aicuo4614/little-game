using System;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Prefabs
{
    internal static class PrefabHandlers
    {
        public static void Create(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, PrefabOps.Create(
                    parameters?["instanceId"]?.Value<int?>(),
                    parameters?["path"]?.ToString(),
                    parameters?["name"]?.ToString(),
                    parameters?["prefabPath"]?.ToString(),
                    parameters?["createVariant"]?.Value<bool>() ?? false));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Instantiate(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, PrefabOps.Instantiate(
                    parameters?["prefabPath"]?.ToString(),
                    ReadVec3(parameters?["position"], Vector3.zero),
                    ReadVec3(parameters?["rotation"], Vector3.zero),
                    ReadVec3(parameters?["scale"], Vector3.one),
                    parameters?["instanceName"]?.ToString(),
                    parameters?["parent"]?.ToString(),
                    parameters?["parentPath"]?.ToString(),
                    parameters?["parentInstanceId"]?.Value<int?>()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void GetInfo(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, PrefabOps.GetInfo(
                    parameters?["instanceId"]?.Value<int?>(),
                    parameters?["path"]?.ToString(),
                    parameters?["name"]?.ToString(),
                    parameters?["prefabPath"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void OpenStage(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, PrefabOps.OpenStage(parameters?["prefabPath"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Save(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, PrefabOps.Save(parameters?["prefabPath"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void CloseStage(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, PrefabOps.CloseStage(
                    parameters?["saveBeforeClosing"]?.Value<bool>() ?? true));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void GetStageInfo(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, PrefabOps.GetStageInfo()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        private static void Reply(string requestId, McpWebSocketClient client, HandlerResult result)
        {
            if (result.Ok)
                _ = client.SendResponseAsync(requestId, result.Payload);
            else
                _ = client.SendErrorAsync(requestId, result.ErrorCode, result.Error);
        }

        private static Vector3 ReadVec3(JToken token, Vector3 fallback)
        {
            if (token == null || token.Type == JTokenType.Null)
                return fallback;
            return new Vector3(
                token["x"]?.Value<float>() ?? fallback.x,
                token["y"]?.Value<float>() ?? fallback.y,
                token["z"]?.Value<float>() ?? fallback.z);
        }
    }
}
