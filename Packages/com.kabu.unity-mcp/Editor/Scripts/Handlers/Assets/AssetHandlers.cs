using System;
using System.Linq;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;

namespace KabuUnityMcp.Editor.Handlers.Assets
{
    internal static class AssetHandlers
    {
        public static void Find(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var filter = parameters?["filter"]?.ToString() ?? string.Empty;
                var maxResults = parameters?["maxResults"]?.Value<int?>() ?? 20;

                string[] folders = null;
                var foldersToken = parameters?["folders"];
                if (foldersToken != null && foldersToken.Type == JTokenType.Array)
                {
                    folders = foldersToken.Values<string>()
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim().Replace('\\', '/'))
                        .ToArray();
                    if (folders.Length == 0)
                        folders = null;
                }
                else
                {
                    var single = parameters?["folder"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(single))
                        folders = new[] { single.Trim().Replace('\\', '/') };
                }

                Reply(requestId, client, AssetOps.Find(filter, folders, maxResults));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Refresh(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, AssetOps.Refresh()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void CreateFolder(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, AssetOps.CreateFolder(
                    parameters?["parentPath"]?.ToString(),
                    parameters?["folderName"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Copy(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, AssetOps.Copy(
                    parameters?["sourcePath"]?.ToString(),
                    parameters?["destinationPath"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Move(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, AssetOps.Move(
                    parameters?["sourcePath"]?.ToString(),
                    parameters?["destinationPath"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Delete(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, AssetOps.Delete(parameters?["path"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void CreateScript(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, AssetOps.CreateScript(
                    parameters?["path"]?.ToString(),
                    parameters?["contents"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void CreateAsset(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, AssetOps.CreateAsset(
                    parameters?["path"]?.ToString(),
                    parameters?["assetType"]?.ToString(),
                    parameters?["shaderName"]?.ToString(),
                    parameters?["color"]?.ToString()));
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
