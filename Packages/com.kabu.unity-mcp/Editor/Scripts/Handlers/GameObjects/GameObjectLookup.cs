using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.GameObjects
{
    /// <summary>
    /// Resolve GameObjects by instanceId, hierarchy path, or name.
    /// </summary>
    internal static class GameObjectLookup
    {
        public static GameObject Find(int? instanceId, string path, string name, out string error)
        {
            error = null;

            if (instanceId.HasValue && instanceId.Value != 0)
            {
                var obj = UnityEditor.EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                if (obj == null)
                {
                    error = $"未找到 instanceId={instanceId.Value} 的 GameObject。";
                    return null;
                }
                return obj;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                var go = FindByPath(path.Trim());
                if (go == null)
                {
                    error = $"未找到路径 '{path.Trim()}' 的 GameObject。";
                    return null;
                }
                return go;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var matches = FindAllByName(name.Trim());
                if (matches.Count == 0)
                {
                    error = $"未找到名为 '{name.Trim()}' 的 GameObject。";
                    return null;
                }
                if (matches.Count > 1)
                {
                    error = $"名为 '{name.Trim()}' 的物体有 {matches.Count} 个，请改用 path 或 instanceId。" +
                            $" 例如：{GetPath(matches[0])} (id={matches[0].GetInstanceID()})";
                    return null;
                }
                return matches[0];
            }

            error = "请提供 instanceId、path 或 name 之一。";
            return null;
        }

        public static GameObject FindOptionalParent(int? instanceId, string path, string name, out string error)
        {
            error = null;
            if (!instanceId.HasValue && string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(name))
                return null;
            return Find(instanceId, path, name, out error);
        }

        public static string GetPath(GameObject go)
        {
            if (go == null)
                return null;

            var sb = new StringBuilder(go.name);
            var t = go.transform.parent;
            while (t != null)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }
            return sb.ToString();
        }

        public static object ToInfo(GameObject go, bool includeComponents = false)
        {
            var t = go.transform;
            object components = null;
            if (includeComponents)
            {
                var comps = go.GetComponents<Component>();
                var names = new List<string>(comps.Length);
                foreach (var c in comps)
                {
                    if (c != null)
                        names.Add(c.GetType().Name);
                }
                components = names;
            }

            return new
            {
                name = go.name,
                path = GetPath(go),
                instanceId = go.GetInstanceID(),
                activeSelf = go.activeSelf,
                activeInHierarchy = go.activeInHierarchy,
                tag = go.tag,
                layer = go.layer,
                childCount = t.childCount,
                position = new { x = t.position.x, y = t.position.y, z = t.position.z },
                localPosition = new { x = t.localPosition.x, y = t.localPosition.y, z = t.localPosition.z },
                rotation = new { x = t.eulerAngles.x, y = t.eulerAngles.y, z = t.eulerAngles.z },
                localScale = new { x = t.localScale.x, y = t.localScale.y, z = t.localScale.z },
                components
            };
        }

        private static GameObject FindByPath(string path)
        {
            path = path.Trim('/');
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('/');
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return null;

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0])
                    continue;

                if (parts.Length == 1)
                    return root;

                var found = Walk(root.transform, parts, 1);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static GameObject Walk(Transform parent, string[] parts, int index)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name != parts[index])
                    continue;

                if (index == parts.Length - 1)
                    return child.gameObject;

                var deeper = Walk(child, parts, index + 1);
                if (deeper != null)
                    return deeper;
            }
            return null;
        }

        private static List<GameObject> FindAllByName(string name)
        {
            var result = new List<GameObject>();
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return result;

            foreach (var root in scene.GetRootGameObjects())
                CollectByName(root.transform, name, result);
            return result;
        }

        private static void CollectByName(Transform t, string name, List<GameObject> result)
        {
            if (t.name == name)
                result.Add(t.gameObject);
            for (var i = 0; i < t.childCount; i++)
                CollectByName(t.GetChild(i), name, result);
        }
    }
}
