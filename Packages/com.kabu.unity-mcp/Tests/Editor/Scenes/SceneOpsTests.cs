using KabuUnityMcp.Editor.Handlers.Scenes;
using NUnit.Framework;

namespace KabuUnityMcp.Editor.Tests.Scenes
{
    public class SceneOpsTests
    {
        [Test]
        public void ValidateSceneAssetPath_Empty_Fails()
        {
            var result = SceneOps.ValidateSceneAssetPath("", out _);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("场景资源路径", result.Error);
        }

        [Test]
        public void ValidateSceneAssetPath_NotUnity_Fails()
        {
            var result = SceneOps.ValidateSceneAssetPath("Assets/Foo.prefab", out _);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains(".unity", result.Error);
        }

        [Test]
        public void ValidateSceneAssetPath_Missing_Fails()
        {
            var result = SceneOps.ValidateSceneAssetPath(
                "Assets/DefinitelyMissing_McpTest_ZZZ.unity", out _);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("找不到场景资源", result.Error);
        }

        [Test]
        public void ListOpened_Succeeds()
        {
            var result = SceneOps.ListOpened();
            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsNotNull(result.Payload);
        }

        [Test]
        public void GetData_ActiveScene_Succeeds()
        {
            var result = SceneOps.GetData(null, true, 1);
            Assert.IsTrue(result.Ok, result.Error);
        }

        [Test]
        public void GetData_UnknownScene_Fails()
        {
            var result = SceneOps.GetData("NoSuchOpenedScene_McpZZZ", true, 1);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("未找到已打开场景", result.Error);
        }
    }
}
