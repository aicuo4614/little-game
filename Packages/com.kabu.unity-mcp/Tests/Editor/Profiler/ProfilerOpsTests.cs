using System.IO;
using KabuUnityMcp.Editor.Handlers.Profiler;
using NUnit.Framework;
using UnityEngine;

namespace KabuUnityMcp.Editor.Tests.Profiler
{
    public class ProfilerOpsTests
    {
        [TearDown]
        public void TearDown()
        {
            ProfilerSessionOps.Disable();
        }

        [Test]
        public void Status_ReturnsOk()
        {
            var result = ProfilerSessionOps.Status();
            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsNotNull(result.Payload);
        }

        [Test]
        public void Enable_Disable_RoundTrip()
        {
            var enable = ProfilerSessionOps.Enable(null, false, null);
            Assert.IsTrue(enable.Ok, enable.Error);

            var status = ProfilerSessionOps.Status();
            Assert.IsTrue(status.Ok, status.Error);

            var disable = ProfilerSessionOps.Disable();
            Assert.IsTrue(disable.Ok, disable.Error);
        }

        [Test]
        public void Clear_DoesNotThrow()
        {
            ProfilerSessionOps.Enable(null, false, null);
            var clear = ProfilerSessionOps.Clear();
            Assert.IsTrue(clear.Ok, clear.Error);
        }

        [Test]
        public void ListCounters_Memory_ReturnsOk()
        {
            var result = ProfilerCounterOps.List("Memory", 0, 10, null);
            Assert.IsTrue(result.Ok, result.Error);
        }

        [Test]
        public void ListCounters_UnknownCategory_Fails()
        {
            var result = ProfilerCounterOps.List("NotARealCategory", 0, 10, null);
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void GetFrameTiming_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ProfilerFrameTimingOps.Get());
        }

        [Test]
        public void GetHierarchy_WhenNoFrames_FailsCleanly()
        {
            ProfilerSessionOps.Disable();
            ProfilerSessionOps.Clear();
            var result = ProfilerHierarchyOps.GetTop(null, 0, 10, "selfMs", null, null, null);
            // 无帧时应明确失败，而不是抛异常或空成功
            Assert.IsFalse(result.Ok);
            Assert.IsTrue(
                result.Error.Contains("帧") || result.Error.Contains("Profiler"),
                result.Error);
        }

        [Test]
        public void GetObjectMemory_MissingArgs_Fails()
        {
            var result = ProfilerObjectMemoryOps.Get(null, null, null);
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void MemoryList_ReturnsOk()
        {
            var result = ProfilerMemorySnapshotOps.List(null, 0, 10);
            Assert.IsTrue(result.Ok, result.Error);
        }

        [Test]
        public void MemoryCompare_MissingFiles_Fails()
        {
            var result = ProfilerMemorySnapshotOps.Compare(
                Path.Combine(Application.temporaryCachePath, "mcp_missing_a.snap"),
                Path.Combine(Application.temporaryCachePath, "mcp_missing_b.snap"));
            Assert.IsFalse(result.Ok);
        }
    }
}
