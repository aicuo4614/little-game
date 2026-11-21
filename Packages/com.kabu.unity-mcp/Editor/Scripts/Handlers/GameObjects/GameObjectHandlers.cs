using System;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.GameObjects
{
    /// <summary>WS 适配层：解析参数 → GameObjectOps → 回包。</summary>
    internal static class GameObjectHandlers
    {
        public static void Create(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var result = GameObjectOps.Create(
                    parameters?["name"]?.ToString(),
                    ReadVec3(parameters?["position"], Vector3.zero),
                    ReadVec3(parameters?["rotation"], Vector3.zero),
                    ReadVec3(parameters?["scale"], Vector3.one),
                    parameters?["isLocalSpace"]?.Value<bool>() ?? false,
                    parameters?["parent"]?.ToString(),
                    parameters?["parentPath"]?.ToString(),
                    parameters?["parentInstanceId"]?.Value<int?>(),
                    parameters?["primitiveType"]?.ToString());
                Reply(requestId, client, result);
                if (result.Ok)
                    Debug.Log($"[kabu-unity-mcp] Created GameObject");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[kabu-unity-mcp] createGameObject: {ex.Message}");
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void BatchCreate(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var array = parameters?["gameObjects"] as JArray;
                if (array == null && parameters is JArray direct)
                    array = direct;
                if (array == null && parameters?["gameObjectsJson"] != null)
                {
                    var raw = parameters["gameObjectsJson"].ToString();
                    if (!string.IsNullOrWhiteSpace(raw))
                        array = JArray.Parse(raw);
                }

                var specs = new System.Collections.Generic.List<BatchCreateSpec>();
                if (array != null)
                {
                    foreach (var item in array)
                    {
                        specs.Add(new BatchCreateSpec
                        {
                            Name = item?["name"]?.ToString(),
                            Position = ReadVec3(item?["position"], Vector3.zero),
                            Rotation = ReadVec3(item?["rotation"], Vector3.zero),
                            Scale = ReadVec3(item?["scale"], Vector3.one),
                            IsLocalSpace = item?["isLocalSpace"]?.Value<bool>() ?? false,
                            ParentName = item?["parent"]?.ToString(),
                            ParentPath = item?["parentPath"]?.ToString(),
                            ParentInstanceId = item?["parentInstanceId"]?.Value<int?>(),
                            PrimitiveType = item?["primitiveType"]?.ToString()
                        });
                    }
                }

                Reply(requestId, client, GameObjectOps.BatchCreate(specs));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[kabu-unity-mcp] batchCreateGameObjects: {ex.Message}");
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void CreateInScene(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, GameObjectOps.CreateInScene(
                    parameters?["scenePath"]?.ToString(),
                    parameters?["name"]?.ToString(),
                    ReadVec3(parameters?["position"], Vector3.zero),
                    ReadVec3(parameters?["rotation"], Vector3.zero),
                    ReadVec3(parameters?["scale"], Vector3.one),
                    parameters?["isLocalSpace"]?.Value<bool>() ?? false,
                    parameters?["parent"]?.ToString(),
                    parameters?["parentPath"]?.ToString(),
                    parameters?["parentInstanceId"]?.Value<int?>(),
                    parameters?["primitiveType"]?.ToString(),
                    parameters?["openIfClosed"]?.Value<bool>() ?? true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[kabu-unity-mcp] createGameObjectInScene: {ex.Message}");
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void List(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var includeInactive = parameters?["includeInactive"]?.Value<bool>() ?? true;
                Reply(requestId, client, GameObjectOps.ListScene(includeInactive));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[kabu-unity-mcp] listSceneObjects: {ex.Message}");
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Find(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, GameObjectOps.Find(
                    parameters?["instanceId"]?.Value<int?>(),
                    parameters?["path"]?.ToString(),
                    parameters?["name"]?.ToString(),
                    parameters?["includeComponents"]?.Value<bool>() ?? true));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Destroy(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, GameObjectOps.Destroy(
                    parameters?["instanceId"]?.Value<int?>(),
                    parameters?["path"]?.ToString(),
                    parameters?["name"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Duplicate(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, GameObjectOps.Duplicate(
                    parameters?["instanceId"]?.Value<int?>(),
                    parameters?["path"]?.ToString(),
                    parameters?["name"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void SetParent(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, GameObjectOps.SetParent(
                    parameters?["instanceId"]?.Value<int?>(),
                    parameters?["path"]?.ToString(),
                    parameters?["name"]?.ToString(),
                    parameters?["parent"]?.ToString(),
                    parameters?["parentPath"]?.ToString(),
                    parameters?["parentInstanceId"]?.Value<int?>(),
                    parameters?["unparent"]?.Value<bool>() ?? false,
                    parameters?["worldPositionStays"]?.Value<bool>() ?? true));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Modify(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Vector3? position = null;
                Vector3? rotation = null;
                Vector3? scale = null;
                if (parameters?["position"] != null)
                    position = ReadVec3(parameters["position"], Vector3.zero);
                if (parameters?["rotation"] != null)
                    rotation = ReadVec3(parameters["rotation"], Vector3.zero);
                if (parameters?["scale"] != null)
                    scale = ReadVec3(parameters["scale"], Vector3.one);

                int? layer = null;
                if (parameters?["layer"] != null)
                    layer = parameters["layer"].Value<int>();

                bool? active = null;
                if (parameters?["active"] != null)
                    active = parameters["active"].Value<bool>();

                Reply(requestId, client, GameObjectOps.Modify(
                    parameters?["instanceId"]?.Value<int?>(),
                    parameters?["path"]?.ToString(),
                    parameters?["name"]?.ToString(),
                    parameters?["newName"]?.ToString(),
                    active,
                    parameters?["tag"]?.ToString(),
                    layer,
                    position,
                    rotation,
                    scale,
                    parameters?["isLocalSpace"]?.Value<bool>() ?? false));
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
