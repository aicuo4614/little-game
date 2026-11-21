using System;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using UnityEditor;
using UnityEngine;
using UProfiler = UnityEngine.Profiling.Profiler;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>单个场景物体或资源的运行时内存占用。</summary>
    internal static class ProfilerObjectMemoryOps
    {
        public static HandlerResult Get(int? instanceId, string path, string name)
        {
            // 优先 instanceId / 场景 path / name；否则尝试 AssetDatabase 路径
            if (instanceId.HasValue || !string.IsNullOrWhiteSpace(path) || !string.IsNullOrWhiteSpace(name))
            {
                // Assets/... 走资源路径，不走场景查找
                if (!string.IsNullOrWhiteSpace(path) &&
                    path.Trim().StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    return GetAsset(path.Trim());
                }

                var go = GameObjectLookup.Find(instanceId, path, name, out var error);
                if (go == null)
                {
                    // path 可能是资源：再试一次
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var assetTry = GetAsset(path.Trim());
                        if (assetTry.Ok)
                            return assetTry;
                    }

                    return HandlerResult.Fail(error ?? "未找到对象。");
                }

                return SuccessForObject(go, "scene");
            }

            return HandlerResult.Fail(
                "请提供 instanceId、场景 path/name，或资源路径（如 Assets/Textures/foo.png）。",
                -32602);
        }

        private static HandlerResult GetAsset(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                return HandlerResult.Fail($"未找到资源 '{assetPath}'。");

            return SuccessForObject(asset, "asset");
        }

        private static HandlerResult SuccessForObject(UnityEngine.Object obj, string source)
        {
            long bytes;
            try
            {
                bytes = UProfiler.GetRuntimeMemorySizeLong(obj);
            }
            catch (Exception ex)
            {
                return HandlerResult.Fail($"读取内存失败：{ex.Message}");
            }

            string path = null;
            if (obj is GameObject go)
                path = GameObjectLookup.GetPath(go);
            else
                path = AssetDatabase.GetAssetPath(obj);

            return HandlerResult.Success(new
            {
                message = $"对象内存：{obj.name}",
                name = obj.name,
                type = obj.GetType().FullName,
                instanceId = obj.GetInstanceID(),
                path,
                source,
                sizeBytes = bytes,
                sizeKb = Math.Round(bytes / 1024.0, 3),
                sizeMb = Math.Round(bytes / (1024.0 * 1024.0), 4)
            });
        }
    }
}
