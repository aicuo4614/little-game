using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using KabuUnityMcp.Editor.Handlers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// unity_get_post_process_stack：只读盘点场景后处理相关组件（URP Volume / PostProcess / Beautify 等）。
    /// 用类型名匹配，避免硬依赖可选包程序集。
    /// </summary>
    internal static class PostProcessOps
    {
        public const string Schema = "kabu.perf.postprocess/v1";

        private static readonly string[] InterestingTypeHints =
        {
            "Volume",
            "PostProcessLayer",
            "PostProcessVolume",
            "Beautify",
            "PostProcess",
            "Bloom",
            "Vignette",
            "ColorAdjustments",
            "Tonemapping",
            "DepthOfField",
            "MotionBlur",
            "PaniniProjection",
            "ScreenSpaceAmbientOcclusion",
            "AmbientOcclusion"
        };

        public static HandlerResult GetStack(int maxItems = 80)
        {
            if (maxItems < 1) maxItems = 80;
            if (maxItems > 300) maxItems = 300;

            var scene = SceneManager.GetActiveScene();
            var behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            var items = new List<object>();
            var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var matchedTotal = 0;

            foreach (var mb in behaviours)
            {
                if (mb == null || mb.gameObject.scene != scene) continue;
                var type = mb.GetType();
                if (!IsInteresting(type)) continue;

                matchedTotal++;
                var full = type.FullName ?? type.Name;
                if (!typeCounts.ContainsKey(full)) typeCounts[full] = 0;
                typeCounts[full]++;

                if (items.Count >= maxItems) continue;

                items.Add(new
                {
                    type = full,
                    shortName = type.Name,
                    path = GetPath(mb.transform),
                    enabled = mb.isActiveAndEnabled,
                    gameObjectActive = mb.gameObject.activeInHierarchy,
                    extras = TryReadExtras(mb)
                });
            }

            return HandlerResult.Success(new
            {
                schema = Schema,
                ok = true,
                status = "ok",
                capturedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                scene = new { name = scene.name, path = scene.path },
                data = new
                {
                    itemCount = items.Count,
                    matchedTotal,
                    truncated = matchedTotal > items.Count,
                    typeCounts = typeCounts
                        .OrderByDescending(kv => kv.Value)
                        .Select(kv => new { type = kv.Key, count = kv.Value })
                        .ToList(),
                    items
                }
            });
        }

        internal static bool IsInteresting(Type type)
        {
            var name = type.Name ?? "";
            var full = type.FullName ?? "";
            foreach (var hint in InterestingTypeHints)
            {
                if (name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            if (full.IndexOf("Beautify", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (full.IndexOf("PostProcessing", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (full.StartsWith("UnityEngine.Rendering", StringComparison.Ordinal)
                && name.EndsWith("Volume", StringComparison.Ordinal))
                return true;

            return false;
        }

        private static object TryReadExtras(MonoBehaviour mb)
        {
            try
            {
                var t = mb.GetType();
                var profileProp = t.GetProperty("profile", BindingFlags.Instance | BindingFlags.Public)
                                 ?? t.GetProperty("sharedProfile", BindingFlags.Instance | BindingFlags.Public);
                var isGlobal = t.GetProperty("isGlobal", BindingFlags.Instance | BindingFlags.Public);
                var priority = t.GetProperty("priority", BindingFlags.Instance | BindingFlags.Public);
                string profileName = null;
                if (profileProp != null)
                {
                    var profile = profileProp.GetValue(mb);
                    if (profile is UnityEngine.Object o)
                        profileName = o.name;
                }

                return new
                {
                    profile = profileName,
                    isGlobal = isGlobal?.GetValue(mb),
                    priority = priority?.GetValue(mb)
                };
            }
            catch
            {
                return null;
            }
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
