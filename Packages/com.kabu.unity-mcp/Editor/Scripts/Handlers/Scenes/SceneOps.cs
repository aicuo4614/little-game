using System;
using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.Scenes
{
    /// <summary>Scene 工具业务逻辑（无 WS）。</summary>
    internal static class SceneOps
    {
        public static HandlerResult ValidateSceneAssetPath(string path, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(path))
                return HandlerResult.Fail("请提供场景资源路径，如 Assets/Scenes/Sample.unity", -32602);

            normalized = path.Trim().Replace('\\', '/');
            if (!normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return HandlerResult.Fail("场景路径必须以 .unity 结尾。", -32602);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(normalized) == null)
                return HandlerResult.Fail($"找不到场景资源：{normalized}");

            return HandlerResult.Success(normalized);
        }

        public static HandlerResult ListOpened()
        {
            var list = new List<object>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
                list.Add(ToShallow(SceneManager.GetSceneAt(i)));

            return HandlerResult.Success(new { scenes = list, count = list.Count });
        }

        public static HandlerResult GetData(string sceneName, bool includeRoots, int hierarchyDepth)
        {
            var scene = ResolveOpenedScene(sceneName, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            if (hierarchyDepth < 0)
                hierarchyDepth = 0;

            var roots = new List<object>();
            if (includeRoots)
            {
                foreach (var root in scene.GetRootGameObjects())
                    BuildHierarchy(root.transform, roots, 0, hierarchyDepth);
            }

            return HandlerResult.Success(new
            {
                scene = ToShallow(scene),
                rootCount = scene.rootCount,
                objects = roots
            });
        }

        public static HandlerResult Open(string path, string mode)
        {
            var validate = ValidateSceneAssetPath(path, out var normalized);
            if (!validate.Ok)
                return validate;

            var openMode = string.Equals(mode, "Additive", StringComparison.OrdinalIgnoreCase)
                ? OpenSceneMode.Additive
                : OpenSceneMode.Single;

            var opened = EditorSceneManager.OpenScene(normalized, openMode);
            if (!opened.IsValid())
                return HandlerResult.Fail($"打开场景失败：{normalized}");

            var list = new List<object>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
                list.Add(ToShallow(SceneManager.GetSceneAt(i)));

            return HandlerResult.Success(new
            {
                opened = ToShallow(opened),
                mode = openMode.ToString(),
                scenes = list
            });
        }

        public static HandlerResult Save(string sceneName, string path)
        {
            var scene = ResolveOpenedScene(sceneName, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            if (string.IsNullOrWhiteSpace(path))
                path = scene.path;

            if (string.IsNullOrWhiteSpace(path))
                return HandlerResult.Fail(
                    $"场景「{scene.name}」尚无路径，请传 path（如 Assets/Scenes/New.unity）。", -32602);

            path = path.Trim().Replace('\\', '/');
            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return HandlerResult.Fail("保存路径必须以 .unity 结尾。", -32602);

            var ok = EditorSceneManager.SaveScene(scene, path);
            if (!ok)
                return HandlerResult.Fail($"保存失败：{path}");

            var saved = SceneManager.GetSceneByPath(path);
            return HandlerResult.Success(new
            {
                saved = true,
                scene = ToShallow(saved.IsValid() ? saved : scene)
            });
        }

        public static HandlerResult GetActive()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return HandlerResult.Fail("当前没有有效的激活场景。");

            return HandlerResult.Success(new
            {
                scene = ToShallow(scene),
                message = $"激活场景：{scene.name}"
            });
        }

        public static HandlerResult ListProjectScenes(int maxResults)
        {
            if (maxResults <= 0)
                maxResults = 200;

            var guids = AssetDatabase.FindAssets("t:Scene");
            var list = new List<object>();
            for (var i = 0; i < guids.Length && list.Count < maxResults; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                    continue;
                list.Add(new
                {
                    path,
                    name = System.IO.Path.GetFileNameWithoutExtension(path)
                });
            }

            return HandlerResult.Success(new
            {
                count = list.Count,
                truncated = guids.Length > list.Count,
                scenes = list
            });
        }

        public static HandlerResult SetActive(string sceneIdentifier)
        {
            var scene = ResolveOpenedByNameOrPath(sceneIdentifier, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            if (!scene.isLoaded)
                return HandlerResult.Fail($"场景未加载：{sceneIdentifier}");

            if (!SceneManager.SetActiveScene(scene))
                return HandlerResult.Fail($"设置激活场景失败：{sceneIdentifier}");

            return HandlerResult.Success(new
            {
                scene = ToShallow(scene),
                message = $"已激活场景：{scene.name}"
            });
        }

        public static HandlerResult Close(string sceneIdentifier, bool removeScene)
        {
            var scene = ResolveOpenedByNameOrPath(sceneIdentifier, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            if (SceneManager.sceneCount <= 1)
                return HandlerResult.Fail("不能关闭唯一打开的场景；请先 Additive 打开其它场景。");

            var name = scene.name;
            var path = scene.path;
            var ok = EditorSceneManager.CloseScene(scene, removeScene);
            if (!ok)
                return HandlerResult.Fail($"关闭场景失败：{sceneIdentifier}");

            return HandlerResult.Success(new
            {
                closed = new { name, path },
                remaining = SceneManager.sceneCount,
                message = $"已关闭场景：{name}"
            });
        }

        private static UnityEngine.SceneManagement.Scene ResolveOpenedByNameOrPath(
            string sceneIdentifier, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(sceneIdentifier))
            {
                error = "请提供 sceneName 或 scenePath。";
                return default;
            }

            sceneIdentifier = sceneIdentifier.Trim().Replace('\\', '/');
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.name == sceneIdentifier ||
                    string.Equals(scene.path, sceneIdentifier, StringComparison.OrdinalIgnoreCase))
                    return scene;
            }

            error = $"未找到已打开场景「{sceneIdentifier}」。";
            return default;
        }

        private static UnityEngine.SceneManagement.Scene ResolveOpenedScene(string sceneName, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                var active = SceneManager.GetActiveScene();
                if (!active.IsValid())
                {
                    error = "当前没有有效的激活场景。";
                    return default;
                }
                return active;
            }

            sceneName = sceneName.Trim();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.name == sceneName)
                    return scene;
            }

            error = $"未找到已打开场景「{sceneName}」。请先用 unity_list_opened_scenes 查看。";
            return default;
        }

        private static object ToShallow(UnityEngine.SceneManagement.Scene scene) => new
        {
            name = scene.name,
            path = scene.path,
            isLoaded = scene.isLoaded,
            isDirty = scene.isDirty,
            isActive = scene == SceneManager.GetActiveScene(),
            rootCount = scene.rootCount,
            buildIndex = scene.buildIndex
        };

        private static void BuildHierarchy(
            UnityEngine.Transform t, List<object> list, int depth, int maxDepth)
        {
            list.Add(new
            {
                name = t.name,
                path = GameObjectLookup.GetPath(t.gameObject),
                instanceId = t.gameObject.GetInstanceID(),
                active = t.gameObject.activeInHierarchy,
                depth
            });

            if (depth >= maxDepth)
                return;

            for (var i = 0; i < t.childCount; i++)
                BuildHierarchy(t.GetChild(i), list, depth + 1, maxDepth);
        }
    }
}
