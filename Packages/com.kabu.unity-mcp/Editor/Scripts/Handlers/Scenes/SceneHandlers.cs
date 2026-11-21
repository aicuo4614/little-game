using System;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;

namespace KabuUnityMcp.Editor.Handlers.Scenes
{
    internal static class SceneHandlers
    {
        public static void ListOpened(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, SceneOps.ListOpened()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void GetData(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, SceneOps.GetData(
                    parameters?["sceneName"]?.ToString(),
                    parameters?["includeRoots"]?.Value<bool>() ?? true,
                    parameters?["hierarchyDepth"]?.Value<int>() ?? 2));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Open(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, SceneOps.Open(
                    parameters?["path"]?.ToString(),
                    parameters?["mode"]?.ToString()));
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
                Reply(requestId, client, SceneOps.Save(
                    parameters?["sceneName"]?.ToString(),
                    parameters?["path"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void GetActive(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, SceneOps.GetActive()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void ListProject(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, SceneOps.ListProjectScenes(
                    parameters?["maxResults"]?.Value<int?>() ?? 200));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void SetActive(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var id = parameters?["sceneIdentifier"]?.ToString()
                         ?? parameters?["sceneName"]?.ToString()
                         ?? parameters?["path"]?.ToString();
                Reply(requestId, client, SceneOps.SetActive(id));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Close(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var id = parameters?["sceneIdentifier"]?.ToString()
                         ?? parameters?["sceneName"]?.ToString()
                         ?? parameters?["path"]?.ToString();
                Reply(requestId, client, SceneOps.Close(
                    id,
                    parameters?["removeScene"]?.Value<bool>() ?? true));
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
