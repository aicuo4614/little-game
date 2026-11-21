using System;
using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Editor.Handlers.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.GameObjects
{
    /// <summary>GameObject 工具业务逻辑（无 WS），供 Handler 与单测共用。</summary>
    internal static class GameObjectOps
    {
        public static HandlerResult Create(
            string name,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale,
            bool isLocalSpace,
            string parentName,
            string parentPath,
            int? parentInstanceId,
            string primitiveType)
        {
            if (string.IsNullOrWhiteSpace(name))
                return HandlerResult.Fail("名称不能为空。", -32602);

            name = name.Trim();
            GameObject go;

            if (!string.IsNullOrWhiteSpace(primitiveType) &&
                Enum.TryParse(primitiveType.Trim(), true, out PrimitiveType primType))
            {
                go = GameObject.CreatePrimitive(primType);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            Undo.RegisterCreatedObjectUndo(go, $"MCP Create {name}");

            var parent = GameObjectLookup.FindOptionalParent(
                parentInstanceId, parentPath, parentName, out var parentError);
            if (parentError != null)
            {
                Undo.DestroyObjectImmediate(go);
                return HandlerResult.Fail(parentError);
            }

            if (parent != null)
                Undo.SetTransformParent(go.transform, parent.transform, $"MCP Parent {name}");

            if (isLocalSpace)
            {
                go.transform.localPosition = position;
                go.transform.localEulerAngles = rotation;
                go.transform.localScale = scale;
            }
            else
            {
                go.transform.position = position;
                go.transform.eulerAngles = rotation;
                go.transform.localScale = scale;
            }

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            MarkDirty(go);
            return HandlerResult.Success(GameObjectLookup.ToInfo(go));
        }

        /// <summary>批量创建；单项失败不中断，汇总 created / errors。</summary>
        public static HandlerResult BatchCreate(IReadOnlyList<BatchCreateSpec> items)
        {
            if (items == null || items.Count == 0)
                return HandlerResult.Fail("请提供至少一个物体描述（gameObjects 数组）。", -32602);

            var created = new List<object>();
            var errors = new List<object>();

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null)
                {
                    errors.Add(new { index = i, error = "条目为空。" });
                    continue;
                }

                var result = Create(
                    item.Name,
                    item.Position,
                    item.Rotation,
                    item.Scale,
                    item.IsLocalSpace,
                    item.ParentName,
                    item.ParentPath,
                    item.ParentInstanceId,
                    item.PrimitiveType);

                if (result.Ok)
                    created.Add(result.Payload);
                else
                    errors.Add(new { index = i, name = item.Name, error = result.Error });
            }

            return HandlerResult.Success(new
            {
                created,
                errors,
                createdCount = created.Count,
                errorCount = errors.Count,
                message = $"批量创建完成：成功 {created.Count}，失败 {errors.Count}。"
            });
        }

        /// <summary>在指定场景中创建物体；场景未打开时可 Additive 打开。</summary>
        public static HandlerResult CreateInScene(
            string scenePath,
            string name,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale,
            bool isLocalSpace,
            string parentName,
            string parentPath,
            int? parentInstanceId,
            string primitiveType,
            bool openIfClosed = true)
        {
            var validate = SceneOps.ValidateSceneAssetPath(scenePath, out var normalized);
            if (!validate.Ok)
                return validate;

            var scene = FindOpenedSceneByPath(normalized);
            if (!scene.IsValid())
            {
                if (!openIfClosed)
                    return HandlerResult.Fail($"场景未打开：{normalized}。可设 openIfClosed=true。");

                scene = EditorSceneManager.OpenScene(normalized, OpenSceneMode.Additive);
                if (!scene.IsValid())
                    return HandlerResult.Fail($"打开场景失败：{normalized}");
            }

            var previous = SceneManager.GetActiveScene();
            if (previous != scene && !SceneManager.SetActiveScene(scene))
                return HandlerResult.Fail($"无法激活场景：{normalized}");

            try
            {
                var result = Create(
                    name, position, rotation, scale, isLocalSpace,
                    parentName, parentPath, parentInstanceId, primitiveType);
                if (!result.Ok)
                    return result;

                // Create 已写入激活场景；若因父物体落在其他场景则搬回目标场景
                var go = Selection.activeGameObject;
                if (go != null && go.scene != scene)
                {
                    SceneManager.MoveGameObjectToScene(go, scene);
                    MarkDirty(go);
                }

                return HandlerResult.Success(new
                {
                    scene = new { name = scene.name, path = scene.path },
                    gameObject = result.Payload,
                    message = $"已在场景 {scene.name} 中创建。"
                });
            }
            finally
            {
                if (previous.IsValid() && previous != scene)
                    SceneManager.SetActiveScene(previous);
            }
        }

        private static Scene FindOpenedSceneByPath(string path)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (string.Equals(s.path, path, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return default;
        }

        public static HandlerResult Find(int? instanceId, string path, string name, bool includeComponents)
        {
            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (error != null)
                return HandlerResult.Fail(error);
            return HandlerResult.Success(GameObjectLookup.ToInfo(go, includeComponents));
        }

        public static HandlerResult Destroy(int? instanceId, string path, string name)
        {
            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            var info = new
            {
                name = go.name,
                path = GameObjectLookup.GetPath(go),
                instanceId = go.GetInstanceID()
            };

            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return HandlerResult.Success(new { destroyed = info });
        }

        public static HandlerResult Duplicate(int? instanceId, string path, string name)
        {
            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            var clone = UnityEngine.Object.Instantiate(go, go.transform.parent);
            clone.name = go.name;
            Undo.RegisterCreatedObjectUndo(clone, $"MCP Duplicate {go.name}");
            Selection.activeGameObject = clone;
            MarkDirty(clone);
            return HandlerResult.Success(GameObjectLookup.ToInfo(clone));
        }

        public static HandlerResult SetParent(
            int? instanceId,
            string path,
            string name,
            string parentName,
            string parentPath,
            int? parentInstanceId,
            bool unparent,
            bool worldPositionStays)
        {
            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            Transform parentTransform = null;
            if (!unparent)
            {
                var parent = GameObjectLookup.FindOptionalParent(
                    parentInstanceId, parentPath, parentName, out var parentError);
                if (parentError != null)
                    return HandlerResult.Fail(parentError);
                if (parent == null)
                    return HandlerResult.Fail(
                        "请提供 parent / parentPath / parentInstanceId，或设 unparent=true。", -32602);
                parentTransform = parent.transform;
            }

            Undo.RecordObject(go.transform, $"MCP SetParent {go.name}");
            go.transform.SetParent(parentTransform, worldPositionStays);
            MarkDirty(go);
            return HandlerResult.Success(GameObjectLookup.ToInfo(go));
        }

        public static HandlerResult Modify(
            int? instanceId,
            string path,
            string name,
            string newName,
            bool? active,
            string tag,
            int? layer,
            Vector3? position,
            Vector3? rotation,
            Vector3? scale,
            bool isLocalSpace)
        {
            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            Undo.RecordObject(go, $"MCP Modify {go.name}");
            Undo.RecordObject(go.transform, $"MCP Modify Transform {go.name}");

            var changes = new List<string>();
            if (!string.IsNullOrWhiteSpace(newName) && newName.Trim() != go.name)
            {
                go.name = newName.Trim();
                changes.Add("name");
            }

            if (active.HasValue)
            {
                go.SetActive(active.Value);
                changes.Add("active");
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                go.tag = tag.Trim();
                changes.Add("tag");
            }

            if (layer.HasValue)
            {
                go.layer = layer.Value;
                changes.Add("layer");
            }

            if (position.HasValue)
            {
                if (isLocalSpace) go.transform.localPosition = position.Value;
                else go.transform.position = position.Value;
                changes.Add("position");
            }

            if (rotation.HasValue)
            {
                if (isLocalSpace) go.transform.localEulerAngles = rotation.Value;
                else go.transform.eulerAngles = rotation.Value;
                changes.Add("rotation");
            }

            if (scale.HasValue)
            {
                go.transform.localScale = scale.Value;
                changes.Add("scale");
            }

            if (changes.Count == 0)
                return HandlerResult.Fail(
                    "未提供可修改字段（newName / active / tag / layer / position / rotation / scale）。",
                    -32602);

            MarkDirty(go);
            return HandlerResult.Success(new
            {
                changed = changes,
                gameObject = GameObjectLookup.ToInfo(go)
            });
        }

        public static HandlerResult ListScene(bool includeInactive)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var list = new List<object>();
            foreach (var root in roots)
                BuildList(root.transform, list, 0, includeInactive);

            return HandlerResult.Success(new
            {
                sceneName = scene.name,
                scenePath = scene.path,
                count = list.Count,
                objects = list
            });
        }

        private static void BuildList(Transform t, List<object> list, int depth, bool includeInactive)
        {
            if (!includeInactive && !t.gameObject.activeSelf)
                return;

            list.Add(new
            {
                name = t.name,
                path = GameObjectLookup.GetPath(t.gameObject),
                instanceId = t.gameObject.GetInstanceID(),
                active = t.gameObject.activeInHierarchy,
                depth
            });

            for (var i = 0; i < t.childCount; i++)
                BuildList(t.GetChild(i), list, depth + 1, includeInactive);
        }

        private static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }

    /// <summary>批量创建条目。</summary>
    internal sealed class BatchCreateSpec
    {
        public string Name;
        public Vector3 Position = Vector3.zero;
        public Vector3 Rotation = Vector3.zero;
        public Vector3 Scale = Vector3.one;
        public bool IsLocalSpace;
        public string ParentName;
        public string ParentPath;
        public int? ParentInstanceId;
        public string PrimitiveType;
    }
}
