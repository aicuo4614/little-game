using System;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KabuUnityMcp.Editor.Handlers.Prefabs
{
    /// <summary>Prefab 业务逻辑（无 WS）。</summary>
    internal static class PrefabOps
    {
        public static HandlerResult Create(
            int? instanceId,
            string path,
            string name,
            string prefabPath,
            bool createVariant)
        {
            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            prefabPath = NormalizePrefabPath(prefabPath, out var pathError);
            if (pathError != null)
                return HandlerResult.Fail(pathError, -32602);

            EnsureParentFolder(prefabPath);

            bool success;
            string message;

            if (createVariant)
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    return HandlerResult.Fail("createVariant 要求源物体是 Prefab 实例。", -32602);

                var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                var sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot);
                if (sourcePrefab == null)
                    return HandlerResult.Fail("无法解析源 Prefab。");

                var temp = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
                temp.transform.position = go.transform.position;
                temp.transform.rotation = go.transform.rotation;
                temp.transform.localScale = go.transform.localScale;
                temp.name = System.IO.Path.GetFileNameWithoutExtension(prefabPath);

                PrefabUtility.SaveAsPrefabAsset(temp, prefabPath, out success);
                Object.DestroyImmediate(temp);
                message = $"已创建 Prefab 变体：{prefabPath}";
            }
            else
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(
                    go, prefabPath, InteractionMode.AutomatedAction, out success);
                message = $"已创建 Prefab：{prefabPath}";
            }

            if (!success)
                return HandlerResult.Fail($"创建 Prefab 失败：{prefabPath}");

            if (go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return HandlerResult.Success(new
            {
                prefabPath,
                createVariant,
                gameObject = GameObjectLookup.ToInfo(go),
                message
            });
        }

        public static HandlerResult Instantiate(
            string prefabPath,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale,
            string instanceName,
            string parentName,
            string parentPath,
            int? parentInstanceId)
        {
            prefabPath = NormalizePrefabPath(prefabPath, out var pathError);
            if (pathError != null)
                return HandlerResult.Fail(pathError, -32602);

            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(prefabPath)))
                return HandlerResult.Fail($"找不到 Prefab：{prefabPath}");

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
                return HandlerResult.Fail($"无法加载 Prefab：{prefabPath}");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
            if (instance == null)
                return HandlerResult.Fail($"实例化失败：{prefabPath}");

            Undo.RegisterCreatedObjectUndo(instance, $"MCP Instantiate {prefabPath}");

            instance.transform.position = position;
            instance.transform.eulerAngles = rotation;
            instance.transform.localScale = scale;

            if (!string.IsNullOrWhiteSpace(instanceName))
                instance.name = instanceName.Trim();

            var parent = GameObjectLookup.FindOptionalParent(
                parentInstanceId, parentPath, parentName, out var parentError);
            if (parentError != null)
            {
                Undo.DestroyObjectImmediate(instance);
                return HandlerResult.Fail(parentError);
            }

            if (parent != null)
                Undo.SetTransformParent(instance.transform, parent.transform, "MCP Prefab Parent");

            Selection.activeGameObject = instance;
            if (instance.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(instance.scene);

            return HandlerResult.Success(new
            {
                prefabPath,
                gameObject = GameObjectLookup.ToInfo(instance),
                message = $"已实例化 Prefab：{prefabPath}"
            });
        }

        public static HandlerResult GetInfo(int? instanceId, string path, string name, string prefabPath)
        {
            if (!string.IsNullOrWhiteSpace(prefabPath))
            {
                prefabPath = NormalizePrefabPath(prefabPath, out var pathError);
                if (pathError != null)
                    return HandlerResult.Fail(pathError, -32602);

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (asset == null)
                    return HandlerResult.Fail($"找不到 Prefab：{prefabPath}");

                return HandlerResult.Success(new
                {
                    kind = "asset",
                    prefabPath,
                    name = asset.name,
                    isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(asset),
                    rootComponentCount = asset.GetComponents<Component>().Length
                });
            }

            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            return HandlerResult.Success(new
            {
                kind = "instance",
                gameObject = GameObjectLookup.ToInfo(go),
                isPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(go),
                isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(go),
                isVariant = PrefabUtility.IsPartOfVariantPrefab(go),
                hasOverrides = PrefabUtility.HasPrefabInstanceAnyOverrides(go, false),
                assetPath = string.IsNullOrEmpty(assetPath) ? null : assetPath
            });
        }

        public static HandlerResult OpenStage(string prefabPath)
        {
            prefabPath = NormalizePrefabPath(prefabPath, out var pathError);
            if (pathError != null)
                return HandlerResult.Fail(pathError, -32602);

            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(prefabPath)))
                return HandlerResult.Fail($"找不到 Prefab：{prefabPath}");

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
                return HandlerResult.Fail($"无法加载 Prefab：{prefabPath}");

            var current = PrefabStageUtility.GetCurrentPrefabStage();
            if (current != null)
                StageUtility.GoToMainStage();

            var stage = PrefabStageUtility.OpenPrefab(prefabPath);
            if (stage == null)
                return HandlerResult.Fail($"打开 Prefab Mode 失败：{prefabPath}");

            return HandlerResult.Success(new
            {
                prefabPath = stage.assetPath,
                rootName = stage.prefabContentsRoot != null ? stage.prefabContentsRoot.name : null,
                message = $"已打开 Prefab Mode：{stage.assetPath}"
            });
        }

        public static HandlerResult Save(string prefabPath)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (!string.IsNullOrWhiteSpace(prefabPath))
            {
                prefabPath = NormalizePrefabPath(prefabPath, out var pathError);
                if (pathError != null)
                    return HandlerResult.Fail(pathError, -32602);

                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                    return HandlerResult.Fail($"找不到 Prefab：{prefabPath}");

                var applied = 0;
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (scene.IsValid())
                {
                    foreach (var root in scene.GetRootGameObjects())
                        applied += ApplyOverridesRecursive(root, prefabPath);
                }

                AssetDatabase.SaveAssets();
                return HandlerResult.Success(new
                {
                    prefabPath,
                    appliedInstances = applied,
                    message = applied > 0
                        ? $"已将 {applied} 个实例的覆盖应用到 {prefabPath}"
                        : $"未找到匹配实例；已 SaveAssets：{prefabPath}"
                });
            }

            if (stage == null)
                return HandlerResult.Fail(
                    "当前没有打开的 Prefab Stage；请传 prefabPath 以应用场景实例覆盖，或先 open_prefab。",
                    -32602);

            EditorSceneManager.MarkSceneDirty(stage.scene);
            PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
            AssetDatabase.SaveAssets();
            return HandlerResult.Success(new
            {
                prefabPath = stage.assetPath,
                message = $"已保存 Prefab Stage：{stage.assetPath}"
            });
        }

        public static HandlerResult CloseStage(bool saveBeforeClosing)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
                return HandlerResult.Fail("当前没有打开的 Prefab Stage。");

            var path = stage.assetPath;
            if (saveBeforeClosing)
            {
                EditorSceneManager.MarkSceneDirty(stage.scene);
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                AssetDatabase.SaveAssets();
            }

            StageUtility.GoToMainStage();
            return HandlerResult.Success(new
            {
                prefabPath = path,
                saved = saveBeforeClosing,
                message = saveBeforeClosing
                    ? $"已保存并关闭 Prefab Stage：{path}"
                    : $"已关闭 Prefab Stage（未保存）：{path}"
            });
        }

        public static HandlerResult GetStageInfo()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
                return HandlerResult.Success(new { isOpen = false, message = "当前不在 Prefab Mode。" });

            return HandlerResult.Success(new
            {
                isOpen = true,
                prefabPath = stage.assetPath,
                rootName = stage.prefabContentsRoot != null ? stage.prefabContentsRoot.name : null
            });
        }

        private static int ApplyOverridesRecursive(GameObject obj, string targetAssetPath)
        {
            var applied = 0;
            if (obj == null)
                return 0;

            if (PrefabUtility.IsPartOfPrefabInstance(obj))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(obj);
                if (root == obj)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
                    if (source != null && AssetDatabase.GetAssetPath(source) == targetAssetPath)
                    {
                        PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
                        applied++;
                    }
                }
            }

            foreach (Transform child in obj.transform)
                applied += ApplyOverridesRecursive(child.gameObject, targetAssetPath);
            return applied;
        }

        private static string NormalizePrefabPath(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "请提供 prefabPath，如 Assets/Prefabs/Enemy.prefab";
                return null;
            }

            path = path.Trim().Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                path = "Assets/" + path.TrimStart('/');

            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                path += ".prefab";

            return path;
        }

        private static void EnsureParentFolder(string assetPath)
        {
            var dir = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir) || AssetDatabase.IsValidFolder(dir))
                return;

            var parts = dir.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
