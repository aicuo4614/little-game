using System;
using KabuUnityMcp.Editor.Extensions;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.SkillEditor
{
    /// <summary>技能编辑器 MCP：只解析参数并转发 <see cref="IMcpSkillEditorHost"/>，不引用 TimelineEditor 类型。</summary>
    internal static class SkillEditorHandlers
    {
        private const string NoHostError =
            "Skill editor Host 未注册。请在含 TimelineEditor 的工程中打开 Unity，并确认已编译技能编辑器程序集。";

        [InitializeOnLoadMethod]
        private static void RegisterMethods()
        {
            McpMethodRegistry.Register("unity.skillEditorOpen", Open);
            McpMethodRegistry.Register("unity.skillEditorStatus", Status);
            McpMethodRegistry.Register("unity.skillEditorLoad", Load);
            McpMethodRegistry.Register("unity.skillEditorSetPreview", SetPreview);
            McpMethodRegistry.Register("unity.skillEditorPlay", Play);
            McpMethodRegistry.Register("unity.skillEditorPause", Pause);
            McpMethodRegistry.Register("unity.skillEditorStop", Stop);
            McpMethodRegistry.Register("unity.skillEditorSeek", Seek);
            McpMethodRegistry.Register("unity.skillEditorListClips", ListClips);
            McpMethodRegistry.Register("unity.skillEditorGetClip", GetClip);
            McpMethodRegistry.Register("unity.skillEditorSnapshot", Snapshot);
            McpMethodRegistry.Register("unity.skillEditorRecord", Record);
        }

        private static IMcpSkillEditorHost RequireHost(string requestId, McpWebSocketClient client)
        {
            var host = McpSkillEditorHostRegistry.Current;
            if (host != null)
            {
                return host;
            }

            _ = client.SendErrorAsync(requestId, -32000, NoHostError);
            return null;
        }

        public static void Open(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                Reply(requestId, client, host.Open());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[kabu-unity-mcp] skillEditorOpen: {ex.Message}");
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Status(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                Reply(requestId, client, host.Status());
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Load(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                var pathOrId = parameters?["pathOrTimelineId"]?.ToString()
                    ?? parameters?["path"]?.ToString()
                    ?? parameters?["timelineId"]?.ToString();
                Reply(requestId, client, host.Load(pathOrId));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void SetPreview(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                Reply(
                    requestId,
                    client,
                    host.SetPreview(
                        parameters?["casterTeam"]?.ToString(),
                        parameters?["quality"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Play(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                var fromStart = parameters?["fromStart"]?.Value<bool>() ?? false;
                Reply(requestId, client, host.Play(fromStart));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Pause(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                Reply(requestId, client, host.Pause());
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Stop(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                Reply(requestId, client, host.Stop());
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Seek(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                var time = parameters?["time"]?.Value<float>() ?? 0f;
                var clipKey = parameters?["clipKey"]?.Value<string>();
                host.Seek(time, clipKey, result => Reply(requestId, client, result));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void ListClips(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                var offset = parameters?["offset"]?.Value<int>() ?? 0;
                var limit = parameters?["limit"]?.Value<int>() ?? 50;
                Reply(requestId, client, host.ListClips(offset, limit));
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
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                Reply(requestId, client, host.GetClip(parameters?["clipKey"]?.ToString()));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Snapshot(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                Reply(requestId, client, host.Snapshot());
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        public static void Record(string requestId, JToken parameters, McpWebSocketClient client)
        {
            try
            {
                var host = RequireHost(requestId, client);
                if (host == null)
                {
                    return;
                }

                var sampleHz = parameters?["sampleHz"]?.Value<float>() ?? 30f;
                var untilCutoff = parameters?["untilCutoff"]?.Value<bool>() ?? true;
                host.RecordPlay(
                    parameters?["casterTeam"]?.ToString(),
                    parameters?["quality"]?.ToString(),
                    sampleHz,
                    untilCutoff,
                    result => Reply(requestId, client, result));
            }
            catch (Exception ex)
            {
                _ = client.SendErrorAsync(requestId, -32000, ex.Message);
            }
        }

        private static void Reply(string requestId, McpWebSocketClient client, McpHostResult result)
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
