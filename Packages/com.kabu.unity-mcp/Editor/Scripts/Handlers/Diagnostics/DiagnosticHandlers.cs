using System;
using KabuUnityMcp.Editor.Extensions;
using KabuUnityMcp.Editor.Handlers.Components;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Diagnostics
{
    internal static class DiagnosticHandlers
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            McpMethodRegistry.Register("unity.extensionCatalog", (id, args, client) => Reply(id, client, () => McpHostResult.Success(new
            {
                schema = "kabu.extensions/v1", projectPath = Application.dataPath,
                unityVersion = Application.unityVersion, packageVersion = KabuUnityMcp.PackageInfo.Version,
                extensions = McpExtensionRegistry.Catalog(args?["extensionId"]?.Value<string>()),
                hosts = new { skillEditor = McpSkillEditorHostRegistry.HasHost, gpuDebug = McpGpuDebugHostRegistry.Current != null }
            })));
            McpMethodRegistry.Register("unity.extensionCall", (id, args, client) => Reply(id, client, () =>
            {
                if (args?["arguments"] != null && args["arguments"].Type != JTokenType.Null && !(args["arguments"] is JObject))
                {
                    return McpHostResult.Fail("arguments must be an object.", -32602);
                }
                return McpExtensionRegistry.Execute(args?["extensionId"]?.Value<string>(), args?["operation"]?.Value<string>(),
                    args?["arguments"] as JObject, args?["allowWrite"]?.Value<bool>() ?? false);
            }));
            McpMethodRegistry.Register("unity.readComponent", (id, args, client) => Reply(id, client, () => Convert(ComponentReadOps.Read(
                args?["instanceId"]?.Value<int?>(), args?["path"]?.Value<string>(), args?["name"]?.Value<string>(),
                args?["componentType"]?.Value<string>(), args?["componentIndex"]?.Value<int>() ?? 0,
                args?["propertyPath"]?.Value<string>(), args?["offset"]?.Value<int>() ?? 0,
                args?["limit"]?.Value<int>() ?? 50, args?["maxDepth"]?.Value<int>() ?? 2))));
            McpMethodRegistry.Register("unity.captureCamera", (id, args, client) => Reply(id, client, () => Convert(CameraCaptureOps.Capture(
                args?["cameraInstanceId"]?.Value<int>() ?? 0, args?["width"]?.Value<int>() ?? 1280, args?["height"]?.Value<int>() ?? 720))));
        }

        private static McpHostResult Convert(HandlerResult result) => result.Ok ? McpHostResult.Success(result.Payload) : McpHostResult.Fail(result.Error, result.ErrorCode);

        private static void Reply(string id, McpWebSocketClient client, Func<McpHostResult> operation)
        {
            try
            {
                var result = operation();
                if (result.Ok)
                {
                    _ = client.SendResponseAsync(id, result.Payload);
                }
                else
                {
                    _ = client.SendErrorAsync(id, result.ErrorCode, result.Error);
                }
            }
            catch (Exception exception)
            {
                _ = client.SendErrorAsync(id, exception is ArgumentException ? -32602 : -32000, exception.GetBaseException().Message);
            }
        }
    }
}
