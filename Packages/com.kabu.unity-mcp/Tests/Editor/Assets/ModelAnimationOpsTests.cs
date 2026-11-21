using KabuUnityMcp.Editor.Handlers.Assets;
using NUnit.Framework;

namespace KabuUnityMcp.Editor.Tests.Assets
{
    public class ModelAnimationOpsTests
    {
        [Test]
        public void ListClips_EmptyPath_Fails()
        {
            var result = ModelAnimationOps.ListClips("  ", 0, 10);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("路径", result.Error);
        }

        [Test]
        public void ListClips_LimitZero_Fails()
        {
            var result = ModelAnimationOps.ListClips("Assets/Missing.fbx", 0, 0);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("limit", result.Error);
        }

        [Test]
        public void SetClip_NoFields_Fails()
        {
            var result = ModelAnimationOps.SetClip(
                "Assets/Missing.fbx",
                "Idle",
                null,
                new ModelClipPatch());
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("没有要修改", result.Error);
        }

        [Test]
        public void ParseRotationBasedUpon_BodyOrientation()
        {
            var error = ModelAnimationOps.ParseRotationBasedUpon("Body Orientation", out var body);
            Assert.IsNull(error);
            Assert.IsTrue(body);
        }

        [Test]
        public void ParsePositionYBasedUpon_Feet()
        {
            var error = ModelAnimationOps.ParsePositionYBasedUpon("Feet", out var com, out var feet);
            Assert.IsNull(error);
            Assert.IsFalse(com);
            Assert.IsTrue(feet);
        }

        [Test]
        public void ParsePositionXZBasedUpon_RejectsFeet()
        {
            var error = ModelAnimationOps.ParsePositionXZBasedUpon("Feet", out _);
            Assert.IsNotNull(error);
        }
    }
}
