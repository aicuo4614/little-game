using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using KabuUnityMcp.Editor.Handlers.Prefabs;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Tests.Prefabs
{
    public class PrefabOpsTests
    {
        private const string TempPrefab = "Assets/kabu-unity-mcp/_TestTemp/McpTestCube.prefab";
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            // Prefab Mode 打开时删资源会怪，先关 Stage
            var stage = PrefabOps.GetStageInfo();
            if (stage.Ok && stage.Payload != null)
            {
                // Close without save if open — CloseStage fails when not open
                PrefabOps.CloseStage(false);
            }

            foreach (var go in _created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            _created.Clear();

            TestAssetCleanup.Delete(TempPrefab);
            TestAssetCleanup.Delete("Assets/kabu-unity-mcp/_TestTemp");
        }

        [Test]
        public void Create_MissingPath_Fails()
        {
            var go = new GameObject("McpPrefab_Src");
            _created.Add(go);
            var result = PrefabOps.Create(go.GetInstanceID(), null, null, "", false);
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void Create_ThenInstantiate_ThenGetInfo()
        {
            if (!AssetDatabase.IsValidFolder("Assets/kabu-unity-mcp"))
                AssetDatabase.CreateFolder("Assets", "kabu-unity-mcp");

            var go = new GameObject("McpPrefab_Cube");
            _created.Add(go);

            var create = PrefabOps.Create(go.GetInstanceID(), null, null, TempPrefab, false);
            Assert.IsTrue(create.Ok, create.Error);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPrefab));

            var infoAsset = PrefabOps.GetInfo(null, null, null, TempPrefab);
            Assert.IsTrue(infoAsset.Ok, infoAsset.Error);

            var inst = PrefabOps.Instantiate(
                TempPrefab, new Vector3(1, 2, 3), Vector3.zero, Vector3.one,
                "McpPrefab_Inst", null, null, null);
            Assert.IsTrue(inst.Ok, inst.Error);

            var found = GameObjectLookup.Find(null, null, "McpPrefab_Inst", out var err);
            Assert.IsNull(err);
            Assert.IsNotNull(found);
            _created.Add(found);
            Assert.AreEqual(1f, found.transform.position.x, 0.001f);

            var infoInst = PrefabOps.GetInfo(found.GetInstanceID(), null, null, null);
            Assert.IsTrue(infoInst.Ok, infoInst.Error);
        }

        [Test]
        public void OpenStage_Save_Close_RoundTrip()
        {
            if (!AssetDatabase.IsValidFolder("Assets/kabu-unity-mcp"))
                AssetDatabase.CreateFolder("Assets", "kabu-unity-mcp");

            var go = new GameObject("McpPrefab_Stage");
            _created.Add(go);
            Assert.IsTrue(PrefabOps.Create(go.GetInstanceID(), null, null, TempPrefab, false).Ok);

            var open = PrefabOps.OpenStage(TempPrefab);
            Assert.IsTrue(open.Ok, open.Error);

            var info = PrefabOps.GetStageInfo();
            Assert.IsTrue(info.Ok, info.Error);

            var save = PrefabOps.Save(null);
            Assert.IsTrue(save.Ok, save.Error);

            var close = PrefabOps.CloseStage(true);
            Assert.IsTrue(close.Ok, close.Error);

            var after = PrefabOps.CloseStage(false);
            Assert.IsFalse(after.Ok);
        }

        [Test]
        public void OpenStage_Missing_Fails()
        {
            var result = PrefabOps.OpenStage("Assets/kabu-unity-mcp/_TestTemp/NoSuch.prefab");
            Assert.IsFalse(result.Ok);
        }
    }
}
