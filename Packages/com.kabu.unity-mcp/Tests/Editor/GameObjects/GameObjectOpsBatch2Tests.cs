using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Tests.GameObjects
{
    public class GameObjectOpsBatch2Tests
    {
        private const string TempScene = "Assets/kabu-unity-mcp/_TestTemp/Batch2Scene.unity";
        private const string TargetScene = "Assets/kabu-unity-mcp/_TestTemp/Batch2Target.unity";
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/kabu-unity-mcp"))
                AssetDatabase.CreateFolder("Assets", "kabu-unity-mcp");
            if (!AssetDatabase.IsValidFolder("Assets/kabu-unity-mcp/_TestTemp"))
                AssetDatabase.CreateFolder("Assets/kabu-unity-mcp", "_TestTemp");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, TempScene);

            var target = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(target, TargetScene);
            EditorSceneManager.CloseScene(target, true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            _created.Clear();

            while (SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(SceneManager.GetSceneAt(SceneManager.sceneCount - 1), true);

            TestAssetCleanup.Delete(TempScene);
            TestAssetCleanup.Delete(TargetScene);
            TestAssetCleanup.Delete("Assets/kabu-unity-mcp/_TestTemp");
        }

        private void Track(GameObject go)
        {
            if (go != null)
                _created.Add(go);
        }

        [Test]
        public void BatchCreate_Empty_Fails()
        {
            var result = GameObjectOps.BatchCreate(new List<BatchCreateSpec>());
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("至少一个", result.Error);
        }

        [Test]
        public void BatchCreate_TwoItems_Succeeds()
        {
            var result = GameObjectOps.BatchCreate(new List<BatchCreateSpec>
            {
                new BatchCreateSpec { Name = "McpBatch_A", Position = new Vector3(1, 0, 0) },
                new BatchCreateSpec { Name = "McpBatch_B", PrimitiveType = "Cube" }
            });
            Assert.IsTrue(result.Ok, result.Error);

            var a = GameObjectLookup.Find(null, null, "McpBatch_A", out _);
            var b = GameObjectLookup.Find(null, null, "McpBatch_B", out _);
            Track(a);
            Track(b);
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.IsNotNull(b.GetComponent<MeshFilter>());
        }

        [Test]
        public void BatchCreate_PartialFailure_ReportsErrors()
        {
            var result = GameObjectOps.BatchCreate(new List<BatchCreateSpec>
            {
                new BatchCreateSpec { Name = "McpBatch_Ok" },
                new BatchCreateSpec { Name = "" }
            });
            Assert.IsTrue(result.Ok, result.Error);

            var ok = GameObjectLookup.Find(null, null, "McpBatch_Ok", out _);
            Track(ok);
            Assert.IsNotNull(ok);
        }

        [Test]
        public void CreateInScene_EmptyPath_Fails()
        {
            var result = GameObjectOps.CreateInScene(
                "", "McpInScene", Vector3.zero, Vector3.zero, Vector3.one,
                false, null, null, null, null);
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void CreateInScene_OpensAdditiveAndCreates()
        {
            var previous = SceneManager.GetActiveScene();
            var result = GameObjectOps.CreateInScene(
                TargetScene, "McpInScene_Go", new Vector3(2, 3, 4), Vector3.zero, Vector3.one,
                false, null, null, null, null, true);
            Assert.IsTrue(result.Ok, result.Error);

            Assert.AreEqual(previous.path, SceneManager.GetActiveScene().path);

            var target = default(Scene);
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == TargetScene)
                    target = s;
            }
            Assert.IsTrue(target.IsValid());

            GameObject found = null;
            foreach (var root in target.GetRootGameObjects())
            {
                if (root.name == "McpInScene_Go")
                    found = root;
            }
            Assert.IsNotNull(found);
            Track(found);
            Assert.AreEqual(2f, found.transform.position.x, 0.001f);
        }

        [Test]
        public void CreateInScene_ClosedWithoutOpen_Fails()
        {
            var result = GameObjectOps.CreateInScene(
                TargetScene, "McpInScene_NoOpen", Vector3.zero, Vector3.zero, Vector3.one,
                false, null, null, null, null, false);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("未打开", result.Error);
        }
    }
}
