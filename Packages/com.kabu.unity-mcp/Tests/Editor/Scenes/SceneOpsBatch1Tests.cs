using KabuUnityMcp.Editor.Handlers.Scenes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Tests.Scenes
{
    public class SceneOpsBatch1Tests
    {
        private const string TempRoot = "Assets/kabu-unity-mcp/_TestTemp";
        private const string TempScene = TempRoot + "/Batch1Scene.unity";
        private const string AdditiveScene = TempRoot + "/Batch1Additive.unity";

        [SetUp]
        public void SetUp()
        {
            // batchmode 起始常是 Untitled；Single NewScene + Save 才能再 Additive
            if (!AssetDatabase.IsValidFolder("Assets/kabu-unity-mcp"))
                AssetDatabase.CreateFolder("Assets", "kabu-unity-mcp");
            if (!AssetDatabase.IsValidFolder(TempRoot))
                AssetDatabase.CreateFolder("Assets/kabu-unity-mcp", "_TestTemp");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, TempScene);
        }

        [TearDown]
        public void TearDown()
        {
            while (SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(SceneManager.GetSceneAt(SceneManager.sceneCount - 1), true);

            TestAssetCleanup.Delete(TempScene);
            TestAssetCleanup.Delete(AdditiveScene);
            TestAssetCleanup.Delete(TempRoot);
        }

        [Test]
        public void GetActive_Succeeds()
        {
            var result = SceneOps.GetActive();
            Assert.IsTrue(result.Ok, result.Error);
        }

        [Test]
        public void ListProjectScenes_Succeeds()
        {
            var result = SceneOps.ListProjectScenes(50);
            Assert.IsTrue(result.Ok, result.Error);
        }

        [Test]
        public void SetActive_Unknown_Fails()
        {
            var result = SceneOps.SetActive("NoSuchOpenedScene_McpZZZ");
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void Close_OnlyScene_Fails()
        {
            while (SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(SceneManager.GetSceneAt(SceneManager.sceneCount - 1), true);

            var result = SceneOps.Close(TempScene, true);
            Assert.IsFalse(result.Ok, result.Error);
            StringAssert.Contains("唯一", result.Error);
        }

        [Test]
        public void Close_Additive_Succeeds()
        {
            var additive = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            Assert.IsTrue(EditorSceneManager.SaveScene(additive, AdditiveScene));
            Assert.Greater(SceneManager.sceneCount, 1);

            var result = SceneOps.Close(AdditiveScene, true);
            Assert.IsTrue(result.Ok, result.Error);

            TestAssetCleanup.Delete(AdditiveScene);
        }

        [Test]
        public void SetActive_Additive_Succeeds()
        {
            var additive = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            Assert.IsTrue(EditorSceneManager.SaveScene(additive, AdditiveScene));

            var addSc = SceneManager.GetSceneByPath(AdditiveScene);
            Assert.IsTrue(addSc.IsValid() && addSc.isLoaded, "additive 应已加载");
            Assume.That(SceneManager.SetActiveScene(addSc), "预置激活 additive 失败则跳过");

            // 再通过 Ops 切回主测试场景
            var set = SceneOps.SetActive(TempScene);
            Assert.IsTrue(set.Ok, set.Error);
            Assert.AreEqual(TempScene, SceneManager.GetActiveScene().path);

            if (addSc.IsValid())
                EditorSceneManager.CloseScene(addSc, true);
            TestAssetCleanup.Delete(AdditiveScene);
        }
    }
}
