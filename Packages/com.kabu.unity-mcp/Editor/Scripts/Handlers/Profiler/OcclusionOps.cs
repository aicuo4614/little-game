using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KabuUnityMcp.Editor.Handlers;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// unity_get_occlusion_info：只读遮挡剔除结论（Area/Portal、烘焙资源线索）。
    /// 缺数据时仍 ok=true 并写明 conclusion/gaps，对齐编排方案 A。
    /// </summary>
    internal static class OcclusionOps
    {
        public const string Schema = "kabu.perf.occlusion/v1";

        public static HandlerResult GetInfo()
        {
            var scene = SceneManager.GetActiveScene();
            var areas = UnityEngine.Object.FindObjectsByType<OcclusionArea>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(a => a && a.gameObject.scene == scene)
                .Select(a => new
                {
                    path = GetPath(a.transform),
                    size = a.size,
                    center = a.center
                })
                .Take(50)
                .Cast<object>()
                .ToList();

            var portals = UnityEngine.Object.FindObjectsByType<OcclusionPortal>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(p => p && p.gameObject.scene == scene)
                .Select(p => new
                {
                    path = GetPath(p.transform),
                    open = p.open
                })
                .Take(50)
                .Cast<object>()
                .ToList();

            var bakeAssets = AssetDatabase.FindAssets("t:OcclusionCullingData")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Take(20)
                .ToList();

            string conclusion;
            var gaps = new List<string>();
            if (bakeAssets.Count > 0 && (areas.Count > 0 || portals.Count > 0))
                conclusion = "likely_enabled";
            else if (bakeAssets.Count > 0)
                conclusion = "bake_data_present";
            else if (areas.Count > 0 || portals.Count > 0)
                conclusion = "components_without_known_bake";
            else
            {
                conclusion = "unknown_or_disabled";
                gaps.Add("no OcclusionArea/Portal and no OcclusionCullingData assets found");
            }

            if (StaticOcclusionCulling.isRunning)
                gaps.Add("StaticOcclusionCulling.isRunning=true (bake in progress)");

            return HandlerResult.Success(new
            {
                schema = Schema,
                ok = true,
                status = "ok",
                conclusion,
                gaps,
                capturedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                scene = new { name = scene.name, path = scene.path },
                data = new
                {
                    isBakeRunning = StaticOcclusionCulling.isRunning,
                    occlusionAreaCount = areas.Count,
                    occlusionPortalCount = portals.Count,
                    occlusionCullingDataAssets = bakeAssets,
                    sampleAreas = areas,
                    samplePortals = portals
                }
            });
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "";
            var stack = new Stack<string>();
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }

            return string.Join("/", stack);
        }
    }
}
