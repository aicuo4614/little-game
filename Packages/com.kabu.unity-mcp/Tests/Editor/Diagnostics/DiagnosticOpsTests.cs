using System;
using System.Linq;
using KabuUnityMcp.Editor.Extensions;
using KabuUnityMcp.Editor.Handlers.Components;
using KabuUnityMcp.Editor.Handlers.Diagnostics;
using KabuUnityMcp.Editor.Integrations;
using KabuUnityMcp.Editor.Integrations.Spine;
using KabuUnityMcp.Editor.Integrations.FairyGui;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace KabuUnityMcp.Editor.Tests.Diagnostics
{
    public class DiagnosticOpsTests
    {
        private sealed class Extension : IMcpExtension
        {
            public string Id => "test.diagnostics";
            public string Version => "1";
            public string Description => "Test adapter";
            public string UnavailableReason { get; set; }
            public int Executions;
            public McpExtensionOperation[] Operations => new[]
            {
                new McpExtensionOperation("read", "Read", true, Schema()),
                new McpExtensionOperation("write", "Write", false, Schema())
            };
            private static JObject Schema() => JObject.Parse("{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}");
            public McpHostResult Execute(string operation, JObject arguments)
            {
                Executions++;
                arguments["value"] = 999;
                return McpHostResult.Success(new { operation });
            }
        }

        [Test]
        public void Extension_RejectsWritesUntilExplicitlyEnabled_AndClonesArguments()
        {
            var extension = new Extension();
            McpExtensionRegistry.Register(extension);
            try
            {
                var arguments = new JObject { ["value"] = 7 };
                Assert.IsFalse(McpExtensionRegistry.Execute(extension.Id, "write", arguments, false).Ok);
                Assert.AreEqual(0, extension.Executions);
                Assert.IsTrue(McpExtensionRegistry.Execute(extension.Id, "write", arguments, true).Ok);
                Assert.AreEqual(7, (int)arguments["value"]);
            }
            finally
            {
                McpExtensionRegistry.Unregister(extension);
            }
        }

        [Test]
        public void Extension_RejectsInvalidArgumentsAndUnknownOperationsBeforeDispatch()
        {
            var extension = new Extension();
            McpExtensionRegistry.Register(extension);
            try
            {
                foreach (var arguments in new[] { new JObject(), new JObject { ["value"] = "7" }, new JObject { ["value"] = 7, ["extra"] = true } })
                {
                    var result = McpExtensionRegistry.Execute(extension.Id, "read", arguments, false);
                    Assert.IsFalse(result.Ok);
                    Assert.AreEqual(-32602, result.ErrorCode);
                }
                Assert.IsFalse(McpExtensionRegistry.Execute(extension.Id, "missing", new JObject(), true).Ok);
                Assert.AreEqual(0, extension.Executions);
            }
            finally
            {
                McpExtensionRegistry.Unregister(extension);
            }
        }

        [Test]
        public void Extension_DuplicateOwnerCannotReplaceOrUnregisterExistingAdapter()
        {
            var original = new Extension();
            var replacement = new Extension();
            McpExtensionRegistry.Register(original);
            try
            {
                Assert.Throws<InvalidOperationException>(() => McpExtensionRegistry.Register(replacement));
                McpExtensionRegistry.Unregister(replacement);
                original.UnavailableReason = "Missing project dependency";
                var result = McpExtensionRegistry.Execute(original.Id, "read", new JObject { ["value"] = 1 }, false);
                Assert.IsFalse(result.Ok);
                Assert.AreEqual(original.UnavailableReason, result.Error);
                Assert.AreEqual(0, original.Executions);
            }
            finally
            {
                McpExtensionRegistry.Unregister(original);
            }
        }

        [Test]
        public void ComponentRead_ReadsNestedSerializedPath_WithoutChangingTransform()
        {
            var go = new GameObject("McpDiagnosticRead");
            try
            {
                go.transform.localPosition = new Vector3(3, 4, 5);
                var result = ComponentReadOps.Read(go.GetInstanceID(), null, null, "Transform", 0, "m_LocalPosition", 0, 10, 1);
                Assert.IsTrue(result.Ok, result.Error);
                var payload = JObject.FromObject(result.Payload);
                Assert.AreEqual("m_LocalPosition", (string)payload["properties"][0]["path"]);
                Assert.AreEqual(3, (float)payload["properties"][0]["value"]["x"]);
                Assert.AreEqual(new Vector3(3, 4, 5), go.transform.localPosition);
                Assert.IsFalse(ComponentReadOps.Read(go.GetInstanceID(), null, null, "Transform", 1, null, 0, 10, 1).Ok);
                Assert.IsFalse(ComponentReadOps.Read(go.GetInstanceID(), null, null, "Transform", 0, "missing", 0, 10, 1).Ok);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ComponentRead_PagesWithoutDuplicatingProperties_AndRejectsUnboundedQueries()
        {
            var go = new GameObject("McpDiagnosticPage");
            try
            {
                var first = JObject.FromObject(ComponentReadOps.Read(go.GetInstanceID(), null, null, "Transform", 0, null, 0, 2, 0).Payload);
                Assert.AreEqual(2, first["properties"].Count());
                Assert.AreEqual(2, (int)first["nextOffset"]);
                var second = JObject.FromObject(ComponentReadOps.Read(go.GetInstanceID(), null, null, "Transform", 0, null, 2, 2, 0).Payload);
                Assert.AreNotEqual((string)first["properties"][1]["path"], (string)second["properties"][0]["path"]);
                Assert.IsFalse(ComponentReadOps.Read(go.GetInstanceID(), null, null, "Transform", 0, null, 0, 201, 0).Ok);
                Assert.IsFalse(ComponentReadOps.Read(go.GetInstanceID(), null, null, "Transform", 0, null, -1, 2, 0).Ok);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CameraCapture_RejectsInvalidTargetAndDimensionsBeforeRendering()
        {
            Assert.IsFalse(CameraCaptureOps.Capture(0, 1280, 720).Ok);
            Assert.IsFalse(CameraCaptureOps.Capture(0, 100000, 720).Ok);
        }

        [Test]
        public void OptionalIntegrations_ReportMissingDependenciesWithoutLoadingThem()
        {
            var spine = new SpineMcpExtension();
            if (OptionalIntegrationReflection.FindType("Spine.Unity.SkeletonRenderer") == null
                && OptionalIntegrationReflection.FindType("Spine.Unity.SkeletonGraphic") == null)
            {
                Assert.IsNotEmpty(spine.UnavailableReason);
            }
            var fairy = new FairyGuiMcpExtension();
            if (OptionalIntegrationReflection.FindType("FairyGUI.GRoot") == null)
            {
                Assert.IsNotEmpty(fairy.UnavailableReason);
            }
            Assert.IsNotEmpty(spine.Operations);
            Assert.IsNotEmpty(fairy.Operations);
        }
    }
}
