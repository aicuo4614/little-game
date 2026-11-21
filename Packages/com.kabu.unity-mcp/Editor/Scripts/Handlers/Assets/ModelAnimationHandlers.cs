using System;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;

namespace KabuUnityMcp.Editor.Handlers.Assets
{
    internal static class ModelAnimationHandlers
    {
        public static void ListClips(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, ModelAnimationOps.ListClips(
                    parameters?["path"]?.ToString(),
                    parameters?["offset"]?.Value<int?>() ?? 0,
                    parameters?["limit"]?.Value<int?>() ?? 50));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void GetClip(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, ModelAnimationOps.GetClip(
                    parameters?["path"]?.ToString(),
                    parameters?["clipName"]?.ToString(),
                    parameters?["takeName"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void SetClip(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, ModelAnimationOps.SetClip(
                    parameters?["path"]?.ToString(),
                    parameters?["clipName"]?.ToString(),
                    parameters?["takeName"]?.ToString(),
                    ReadPatch(parameters)));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        private static ModelClipPatch ReadPatch(JToken parameters)
        {
            return new ModelClipPatch
            {
                LoopTime = ReadBool(parameters, "loopTime"),
                LoopPose = ReadBool(parameters, "loopPose"),
                CycleOffset = ReadFloat(parameters, "cycleOffset"),
                RootRotationBakeIntoPose = ReadBool(parameters, "rootRotationBakeIntoPose"),
                RootRotationBasedUpon = ReadString(parameters, "rootRotationBasedUpon"),
                RootRotationOffset = ReadFloat(parameters, "rootRotationOffset"),
                RootPositionYBakeIntoPose = ReadBool(parameters, "rootPositionYBakeIntoPose"),
                RootPositionYBasedUpon = ReadString(parameters, "rootPositionYBasedUpon"),
                RootPositionYOffset = ReadFloat(parameters, "rootPositionYOffset"),
                RootPositionXZBakeIntoPose = ReadBool(parameters, "rootPositionXZBakeIntoPose"),
                RootPositionXZBasedUpon = ReadString(parameters, "rootPositionXZBasedUpon"),
                Mirror = ReadBool(parameters, "mirror"),
                FirstFrame = ReadFloat(parameters, "firstFrame"),
                LastFrame = ReadFloat(parameters, "lastFrame")
            };
        }

        private static bool? ReadBool(JToken parameters, string name)
        {
            var token = parameters?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            return token.Value<bool>();
        }

        private static float? ReadFloat(JToken parameters, string name)
        {
            var token = parameters?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            return token.Value<float>();
        }

        private static string ReadString(JToken parameters, string name)
        {
            var token = parameters?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            var text = token.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static void Reply(string requestId, McpWebSocketClient client, HandlerResult result)
        {
            if (result.Ok)
            {
                _ = client.SendResponseAsync(requestId, result.Payload);
            }
            else
            {
                _ = client.SendErrorAsync(requestId, result.ErrorCode, result.Error);
            }
        }
    }
}
