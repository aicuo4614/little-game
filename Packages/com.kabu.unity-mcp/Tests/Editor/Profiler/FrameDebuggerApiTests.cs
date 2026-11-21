using KabuUnityMcp.Editor.Handlers.Profiler;
using NUnit.Framework;

namespace KabuUnityMcp.Editor.Tests.Profiler
{
    public class FrameDebuggerApiTests
    {
        [Test]
        public void Discover_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => FrameDebuggerApi.EnsureDiscovered());
            Assert.IsTrue(
                FrameDebuggerApi.Available,
                FrameDebuggerApi.UnavailableReason ?? "Available=false");
        }

        [Test]
        public void List_WhenDisabled_ReturnsOk()
        {
            var result = FrameDebuggerOps.List(0, 10, null, null);
            Assert.IsTrue(result.Ok, result.Error);
        }
    }
}
