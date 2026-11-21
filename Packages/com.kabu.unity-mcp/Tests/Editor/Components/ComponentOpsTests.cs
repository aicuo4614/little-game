using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers.Components;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using NUnit.Framework;
using UnityEngine;

namespace KabuUnityMcp.Editor.Tests.Components
{
    public class ComponentOpsTests
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

        private GameObject CreateGo(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        [Test]
        public void Resolve_Rigidbody_Succeeds()
        {
            var type = ComponentTypeResolver.Resolve("Rigidbody", out var error);
            Assert.IsNull(error);
            Assert.AreEqual(typeof(Rigidbody), type);
        }

        [Test]
        public void Resolve_Unknown_Fails()
        {
            var type = ComponentTypeResolver.Resolve("NoSuchComponent_McpZZZ", out var error);
            Assert.IsNull(type);
            Assert.IsNotNull(error);
        }

        [Test]
        public void Add_ThenRemove_Rigidbody()
        {
            var go = CreateGo("McpComp_Add");
            var add = ComponentOps.Add(go.GetInstanceID(), null, null, "Rigidbody");
            Assert.IsTrue(add.Ok, add.Error);
            Assert.IsNotNull(go.GetComponent<Rigidbody>());

            var again = ComponentOps.Add(go.GetInstanceID(), null, null, "Rigidbody");
            Assert.IsFalse(again.Ok);

            var remove = ComponentOps.Remove(go.GetInstanceID(), null, null, "Rigidbody");
            Assert.IsTrue(remove.Ok, remove.Error);
            Assert.IsNull(go.GetComponent<Rigidbody>());
        }

        [Test]
        public void Remove_Transform_Fails()
        {
            var go = CreateGo("McpComp_Transform");
            var result = ComponentOps.Remove(go.GetInstanceID(), null, null, "Transform");
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("Transform", result.Error);
        }

        [Test]
        public void SetField_RigidbodyMass()
        {
            var go = CreateGo("McpComp_Mass");
            Assert.IsTrue(ComponentOps.Add(go.GetInstanceID(), null, null, "Rigidbody").Ok);

            var set = ComponentOps.SetField(
                go.GetInstanceID(), null, null, "Rigidbody", "mass", "3.5", "float");
            Assert.IsTrue(set.Ok, set.Error);
            Assert.AreEqual(3.5f, go.GetComponent<Rigidbody>().mass, 0.001f);
        }

        [Test]
        public void ConvertValue_Vector3()
        {
            var v = (Vector3)ComponentOps.ConvertValue("1,2,3", "vector3", typeof(Vector3));
            Assert.AreEqual(1f, v.x, 0.001f);
            Assert.AreEqual(2f, v.y, 0.001f);
            Assert.AreEqual(3f, v.z, 0.001f);
        }
    }
}
