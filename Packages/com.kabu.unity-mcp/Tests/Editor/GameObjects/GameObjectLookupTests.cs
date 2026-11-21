using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using NUnit.Framework;
using UnityEngine;

namespace KabuUnityMcp.Editor.Tests.GameObjects
{
    public class GameObjectLookupTests
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

        private GameObject Create(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            _created.Add(go);
            return go;
        }

        [Test]
        public void Find_ByInstanceId_ReturnsObject()
        {
            var go = Create("McpLookup_ById");
            var found = GameObjectLookup.Find(go.GetInstanceID(), null, null, out var error);
            Assert.IsNull(error);
            Assert.AreSame(go, found);
        }

        [Test]
        public void Find_ByPath_ReturnsNestedObject()
        {
            var parent = Create("McpLookup_Parent");
            var child = Create("McpLookup_Child", parent.transform);

            var found = GameObjectLookup.Find(null, "McpLookup_Parent/McpLookup_Child", null, out var error);
            Assert.IsNull(error);
            Assert.AreSame(child, found);
            Assert.AreEqual("McpLookup_Parent/McpLookup_Child", GameObjectLookup.GetPath(child));
        }

        [Test]
        public void Find_ByName_WhenUnique_ReturnsObject()
        {
            var go = Create("McpLookup_UniqueName");
            var found = GameObjectLookup.Find(null, null, "McpLookup_UniqueName", out var error);
            Assert.IsNull(error);
            Assert.AreSame(go, found);
        }

        [Test]
        public void Find_ByName_WhenAmbiguous_ReturnsError()
        {
            Create("McpLookup_Dup");
            Create("McpLookup_Dup");

            var found = GameObjectLookup.Find(null, null, "McpLookup_Dup", out var error);
            Assert.IsNull(found);
            Assert.IsNotNull(error);
            StringAssert.Contains("改用 path 或 instanceId", error);
        }

        [Test]
        public void Find_Missing_ReturnsError()
        {
            var found = GameObjectLookup.Find(null, null, "McpLookup_DoesNotExist_ZZZ", out var error);
            Assert.IsNull(found);
            Assert.IsNotNull(error);
            StringAssert.Contains("未找到", error);
        }

        [Test]
        public void Find_NoArgs_ReturnsError()
        {
            var found = GameObjectLookup.Find(null, null, null, out var error);
            Assert.IsNull(found);
            Assert.IsNotNull(error);
            StringAssert.Contains("instanceId", error);
        }
    }
}
