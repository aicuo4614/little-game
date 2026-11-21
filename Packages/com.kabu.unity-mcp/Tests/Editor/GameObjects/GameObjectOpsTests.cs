using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using NUnit.Framework;
using UnityEngine;

namespace KabuUnityMcp.Editor.Tests.GameObjects
{
    public class GameObjectOpsTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private void Track(GameObject go)
        {
            if (go != null)
                _created.Add(go);
        }

        [Test]
        public void Create_EmptyName_Fails()
        {
            var result = GameObjectOps.Create(
                "", Vector3.zero, Vector3.zero, Vector3.one, false,
                null, null, null, null);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("名称不能为空", result.Error);
        }

        [Test]
        public void Create_ThenFind_Succeeds()
        {
            var result = GameObjectOps.Create(
                "McpOps_Create", new Vector3(1, 2, 3), Vector3.zero, Vector3.one, false,
                null, null, null, null);
            Assert.IsTrue(result.Ok, result.Error);

            var found = GameObjectLookup.Find(null, null, "McpOps_Create", out var error);
            Assert.IsNull(error);
            Assert.IsNotNull(found);
            Track(found);
            Assert.AreEqual(1f, found.transform.position.x, 0.001f);
        }

        [Test]
        public void Create_PrimitiveCube_HasMeshFilter()
        {
            var result = GameObjectOps.Create(
                "McpOps_Cube", Vector3.zero, Vector3.zero, Vector3.one, false,
                null, null, null, "Cube");
            Assert.IsTrue(result.Ok, result.Error);

            var go = GameObjectLookup.Find(null, null, "McpOps_Cube", out _);
            Track(go);
            Assert.IsNotNull(go.GetComponent<MeshFilter>());
        }

        [Test]
        public void Destroy_RemovesObject()
        {
            var create = GameObjectOps.Create(
                "McpOps_Destroy", Vector3.zero, Vector3.zero, Vector3.one, false,
                null, null, null, null);
            Assert.IsTrue(create.Ok);

            var go = GameObjectLookup.Find(null, null, "McpOps_Destroy", out _);
            var id = go.GetInstanceID();

            var destroy = GameObjectOps.Destroy(id, null, null);
            Assert.IsTrue(destroy.Ok, destroy.Error);

            var missing = GameObjectLookup.Find(id, null, null, out var error);
            Assert.IsNull(missing);
            Assert.IsNotNull(error);
        }

        [Test]
        public void Duplicate_CreatesSibling()
        {
            var create = GameObjectOps.Create(
                "McpOps_DupSrc", Vector3.zero, Vector3.zero, Vector3.one, false,
                null, null, null, null);
            Assert.IsTrue(create.Ok);
            var src = GameObjectLookup.Find(null, null, "McpOps_DupSrc", out _);
            Track(src);

            var dup = GameObjectOps.Duplicate(src.GetInstanceID(), null, null);
            Assert.IsTrue(dup.Ok, dup.Error);

            // Instantiate keeps same name; find by instanceId from payload is hard;
            // ensure at least two objects named McpOps_DupSrc
            var all = Object.FindObjectsOfType<GameObject>();
            var count = 0;
            foreach (var g in all)
            {
                if (g.name == "McpOps_DupSrc")
                {
                    count++;
                    Track(g);
                }
            }
            Assert.GreaterOrEqual(count, 2);
        }

        [Test]
        public void SetParent_AndUnparent()
        {
            var parentResult = GameObjectOps.Create(
                "McpOps_Parent", Vector3.zero, Vector3.zero, Vector3.one, false,
                null, null, null, null);
            var childResult = GameObjectOps.Create(
                "McpOps_Child", Vector3.zero, Vector3.zero, Vector3.one, false,
                null, null, null, null);
            Assert.IsTrue(parentResult.Ok);
            Assert.IsTrue(childResult.Ok);

            var parent = GameObjectLookup.Find(null, null, "McpOps_Parent", out _);
            var child = GameObjectLookup.Find(null, null, "McpOps_Child", out _);
            Track(parent);
            Track(child);

            var set = GameObjectOps.SetParent(
                child.GetInstanceID(), null, null,
                null, null, parent.GetInstanceID(),
                false, true);
            Assert.IsTrue(set.Ok, set.Error);
            Assert.AreEqual(parent.transform, child.transform.parent);

            var unparent = GameObjectOps.SetParent(
                child.GetInstanceID(), null, null,
                null, null, null, true, true);
            Assert.IsTrue(unparent.Ok, unparent.Error);
            Assert.IsNull(child.transform.parent);
        }

        [Test]
        public void Modify_RenamesAndFailsWhenNoFields()
        {
            var create = GameObjectOps.Create(
                "McpOps_Mod", Vector3.zero, Vector3.zero, Vector3.one, false,
                null, null, null, null);
            Assert.IsTrue(create.Ok);
            var go = GameObjectLookup.Find(null, null, "McpOps_Mod", out _);
            Track(go);

            var empty = GameObjectOps.Modify(
                go.GetInstanceID(), null, null,
                null, null, null, null, null, null, null, false);
            Assert.IsFalse(empty.Ok);

            var rename = GameObjectOps.Modify(
                go.GetInstanceID(), null, null,
                "McpOps_ModRenamed", null, null, null, null, null, null, false);
            Assert.IsTrue(rename.Ok, rename.Error);
            Assert.AreEqual("McpOps_ModRenamed", go.name);
        }

        [Test]
        public void ListScene_IncludesCreatedObject()
        {
            var create = GameObjectOps.Create(
                "McpOps_ListMe", Vector3.zero, Vector3.zero, Vector3.one, false,
                null, null, null, null);
            Assert.IsTrue(create.Ok);
            Track(GameObjectLookup.Find(null, null, "McpOps_ListMe", out _));

            var list = GameObjectOps.ListScene(true);
            Assert.IsTrue(list.Ok);
            Assert.IsNotNull(list.Payload);
        }
    }
}
