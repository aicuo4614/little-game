using System.Reflection;
using KabuUnityMcp.Editor.Handlers.Profiler;
using KabuUnityMcp.Gpu;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace KabuUnityMcp.Editor.Tests.Profiler
{
    public class GpuBufferOpsTests
    {
        [TearDown]
        public void TearDown()
        {
            McpGpuBufferRegistry.Clear();
        }

        [Test]
        public void List_EmptyRegistry_ReturnsZeroTotal()
        {
            var result = GpuBufferOps.List(0, 32, null, refreshHost: false);
            Assert.IsTrue(result.Ok);
            Assert.AreEqual(0, GetInt(result.Payload, "total"));
            Assert.AreEqual(0, GetInt(result.Payload, "returned"));
        }

        [Test]
        public void List_PaginatesRegisteredBuffers()
        {
            var a = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 16);
            var b = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 16);
            try
            {
                McpGpuBufferRegistry.Register("test/a", a, name: "A", schema: "kabu.gpu.skin-range/v1");
                McpGpuBufferRegistry.Register("test/b", b, name: "B", schema: "kabu.gpu.skin-range/v1");

                var page0 = GpuBufferOps.List(0, 1, null, refreshHost: false);
                Assert.IsTrue(page0.Ok);
                Assert.AreEqual(2, GetInt(page0.Payload, "total"));
                Assert.AreEqual(1, GetInt(page0.Payload, "returned"));
                Assert.AreEqual(1, GetInt(page0.Payload, "nextOffset"));

                var page1 = GpuBufferOps.List(1, 10, null, refreshHost: false);
                Assert.AreEqual(1, GetInt(page1.Payload, "returned"));
                Assert.IsNull(GetProp(page1.Payload, "nextOffset"));
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }

        [Test]
        public void NormalizeSlice_ClampsLimit()
        {
            var offset = -3;
            var limit = 9999;
            GpuBufferOps.NormalizeSlice(ref offset, ref limit);
            Assert.AreEqual(0, offset);
            Assert.AreEqual(GpuBufferOps.MaxLimit, limit);
        }

        [Test]
        public void DecodeItems_SkinRangeSchema()
        {
            var bytes = new NativeArray<byte>(16, Allocator.Temp);
            try
            {
                WriteInt(bytes, 0, 10);
                WriteInt(bytes, 4, 20);
                WriteInt(bytes, 8, 30);
                WriteInt(bytes, 12, 0);
                var items = GpuBufferOps.DecodeItems("kabu.gpu.skin-range/v1", bytes, 16, 1);
                Assert.AreEqual(1, items.Count);
                Assert.AreEqual(10, GetInt(items[0], "Base"));
                Assert.AreEqual(20, GetInt(items[0], "Count"));
                Assert.AreEqual(30, GetInt(items[0], "Start"));
            }
            finally
            {
                bytes.Dispose();
            }
        }

        [Test]
        public void SummarizeBytes_DetectsNaN()
        {
            var bytes = new NativeArray<byte>(4, Allocator.Temp);
            try
            {
                var nan = float.NaN;
                var nanBytes = System.BitConverter.GetBytes(nan);
                bytes[0] = nanBytes[0];
                bytes[1] = nanBytes[1];
                bytes[2] = nanBytes[2];
                bytes[3] = nanBytes[3];
                var stats = GpuBufferOps.SummarizeBytes(bytes, 4, 1);
                Assert.AreEqual(1, GetInt(stats, "nanCount"));
            }
            finally
            {
                bytes.Dispose();
            }
        }

        [Test]
        public void PruneDead_RemovesDisposedBuffers()
        {
            var buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16);
            McpGpuBufferRegistry.Register("test/dead", buf);
            buf.Dispose();
            var pruned = McpGpuBufferRegistry.PruneDead();
            Assert.GreaterOrEqual(pruned, 1);
            Assert.IsFalse(McpGpuBufferRegistry.TryGet("test/dead", out _));
        }

        static void WriteInt(NativeArray<byte> data, int offset, int value)
        {
            data[offset] = (byte)(value & 0xff);
            data[offset + 1] = (byte)((value >> 8) & 0xff);
            data[offset + 2] = (byte)((value >> 16) & 0xff);
            data[offset + 3] = (byte)((value >> 24) & 0xff);
        }

        static object GetProp(object obj, string name) =>
            obj?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);

        static int GetInt(object obj, string name)
        {
            var v = GetProp(obj, name);
            Assert.IsNotNull(v, "missing property " + name);
            return System.Convert.ToInt32(v);
        }
    }
}
