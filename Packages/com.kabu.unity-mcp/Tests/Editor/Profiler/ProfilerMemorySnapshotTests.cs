using System;
using System.Collections;
using System.IO;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Editor.Handlers.Profiler;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine.TestTools;

namespace KabuUnityMcp.Editor.Tests.Profiler
{
    public class ProfilerMemorySnapshotTests
    {
        private string directory;
        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "kabu-memory-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }
        [TearDown] public void TearDown()
        {
            if (!ProfilerMemorySnapshotOps.IsBusy && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        [Test] public void Capture_RejectsInvalidFlagsAndTimeoutBeforeStarting()
        {
            foreach (var flags in new[] { 0, -1, 32 })
                Assert.IsFalse(ProfilerMemorySnapshotOps.TryBeginTake(null, null, out _, flags: flags));
            foreach (var timeout in new[] { 0, 9, 601 })
                Assert.IsFalse(ProfilerMemorySnapshotOps.TryBeginTake(null, null, out _, timeoutSeconds: timeout));
            Assert.IsFalse(ProfilerMemorySnapshotOps.IsBusy);
        }
        [Test] public void Capture_RejectsChangedTarget()
        {
            Assert.IsFalse(ProfilerMemorySnapshotOps.TryBeginTake(null, null, out var error,
                expectedTargetId: ProfilerDriver.connectedProfiler ^ 0x40000000));
            StringAssert.Contains("target changed", error);
            Assert.IsFalse(ProfilerMemorySnapshotOps.IsBusy);
        }
        [Test] public void CapturePath_RejectsOverwriteAndWrongExtension()
        {
            var path = Path.Combine(directory, "existing.snap");
            File.WriteAllText(path, "preserve me");
            Assert.Throws<IOException>(() => ProfilerMemorySnapshotOps.NormalizeCapturePath(path));
            Assert.AreEqual("preserve me", File.ReadAllText(path));
            Assert.Throws<ArgumentException>(() => ProfilerMemorySnapshotOps.NormalizeCapturePath(Path.Combine(directory, "bad.txt")));
            Assert.AreNotEqual(ProfilerMemorySnapshotOps.NormalizeCapturePath(null), ProfilerMemorySnapshotOps.NormalizeCapturePath(null));
        }
        [Test] public void List_PaginatesAndReportsCorruptMetadata()
        {
            var path = Path.Combine(directory, "a.snap");
            File.WriteAllText(path, "a");
            File.WriteAllText(path + ".mcp.json", "broken");
            File.WriteAllText(Path.Combine(directory, "b.snap"), "b");
            var first = JObject.FromObject(ProfilerMemorySnapshotOps.List(directory, 0, 1).Payload);
            Assert.AreEqual(2, (int)first["total"]);
            Assert.AreEqual(1, (int)first["nextOffset"]);
            var second = JObject.FromObject(ProfilerMemorySnapshotOps.List(directory, 1, 1).Payload);
            Assert.AreNotEqual((string)first["snapshots"][0]["path"], (string)second["snapshots"][0]["path"]);
            Assert.IsNull(second["nextOffset"].Value<int?>());
            var all = JObject.FromObject(ProfilerMemorySnapshotOps.List(directory, 0, 50).Payload);
            Assert.IsTrue(all.ToString().Contains("Unreadable capture metadata"));
        }
        [Test] public void List_RejectsMetadataForReplacedSnapshot()
        {
            var path = Path.Combine(directory, "replaced.snap");
            File.WriteAllBytes(path, new byte[10]);
            File.WriteAllText(path + ".mcp.json", "{\"sizeBytes\":11,\"target\":{\"targetId\":42}}");
            var json = JObject.FromObject(ProfilerMemorySnapshotOps.List(directory, 0, 50).Payload);
            StringAssert.Contains("does not match", (string)json["snapshots"][0]["captureMetadata"]["warning"]);
            Assert.IsNull(json["snapshots"][0]["captureMetadata"]["target"]);
        }
        [Test] public void List_ReturnsValidCaptureMetadata()
        {
            var path = Path.Combine(directory, "capture.snap");
            File.WriteAllBytes(path, new byte[10]);
            File.WriteAllText(path + ".mcp.json", "{\"sizeBytes\":10,\"target\":{\"targetId\":42}}");
            var json = JObject.FromObject(ProfilerMemorySnapshotOps.List(directory, 0, 50).Payload);
            Assert.AreEqual(42, (int)json["snapshots"][0]["captureMetadata"]["target"]["targetId"]);
        }
        [Test] public void Compare_ExplicitlyLimitsAnalysisToFiles()
        {
            var a = Path.Combine(directory, "a.snap");
            var b = Path.Combine(directory, "b.snap");
            File.WriteAllBytes(a, new byte[10]); File.WriteAllBytes(b, new byte[20]);
            var result = ProfilerMemorySnapshotOps.Compare(a, b);
            Assert.IsTrue(result.Ok, result.Error);
            var json = JObject.FromObject(result.Payload);
            Assert.AreEqual("fileMetadataOnly", (string)json["analysisKind"]);
            Assert.IsFalse((bool)json["objectAnalysisAvailable"]);
            Assert.AreEqual(10L, (long)json["delta"]["sizeDeltaBytes"]);
        }
        [Test] public void Targets_ListAndRejectUnknownId()
        {
            Assert.IsTrue(ProfilerMemoryTargetOps.List().Ok);
            Assert.IsFalse(ProfilerMemoryTargetOps.Select(int.MaxValue).Ok);
        }
        [UnityTest] public IEnumerator NativeCapture_SavesSnapshotAndTargetMetadata()
        {
            Assert.IsTrue(ProfilerMemorySnapshotOps.Available, ProfilerMemorySnapshotOps.UnavailableReason);
            var path = Path.Combine(directory, "native.snap");
            HandlerResult? result = null;
            Assert.IsTrue(ProfilerMemorySnapshotOps.TryBeginTake(path, value => result = value, out var error,
                expectedTargetId: ProfilerDriver.connectedProfiler, flags: 2, timeoutSeconds: 30), error);
            Assert.IsFalse(ProfilerMemorySnapshotOps.TryBeginTake(null, null, out _));
            Assert.IsFalse(ProfilerMemoryTargetOps.Select(ProfilerDriver.connectedProfiler).Ok);
            var deadline = UnityEditor.EditorApplication.timeSinceStartup + 40;
            while (!result.HasValue && UnityEditor.EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.IsTrue(result.HasValue, "No capture completion callback.");
            Assert.IsTrue(result.Value.Ok, result.Value.Error);
            Assert.IsFalse(ProfilerMemorySnapshotOps.IsBusy);
            Assert.Greater(new FileInfo(path).Length, 0);
            var metadata = JObject.Parse(File.ReadAllText(path + ".mcp.json"));
            Assert.AreEqual(ProfilerDriver.connectedProfiler, (int)metadata["target"]["targetId"]);
            Assert.AreEqual(2, (int)metadata["captureFlags"]);
        }
    }
}
