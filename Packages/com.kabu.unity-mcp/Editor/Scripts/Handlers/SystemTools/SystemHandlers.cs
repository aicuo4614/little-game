using System;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;

namespace KabuUnityMcp.Editor.Handlers.SystemTools
{
    internal static class SystemHandlers
    {
        public static void GetProjectInfo(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, SystemOps.GetProjectInfo()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void GetPlayModeState(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, SystemOps.GetPlayModeState()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void EnterPlayMode(string requestId, JToken parameters, McpWebSocketClient client)
        {
            PlayModeTransitionHandlers.EnterPlayMode(requestId, parameters, client);
        }

        public static void ExitPlayMode(string requestId, McpWebSocketClient client)
        {
            PlayModeTransitionHandlers.ExitPlayMode(requestId, client);
        }

        public static void StepPlayModeFrame(string requestId, McpWebSocketClient client)
        {
            PlayModeTransitionHandlers.StepPlayModeFrame(requestId, client);
        }

        public static void GetCompilationStatus(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, SystemOps.GetCompilationStatus()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void RunMenuItem(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                Reply(requestId, client, SystemOps.RunMenuItem(parameters?["menuPath"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void TriggerCompilation(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, SystemOps.TriggerCompilation()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
        }

        public static void BringToForeground(string requestId, McpWebSocketClient client)
        {
            try { Reply(requestId, client, SystemOps.BringToForeground()); }
            catch (Exception ex) { _ = client.SendErrorAsync(requestId, -32000, ex.Message); }
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
