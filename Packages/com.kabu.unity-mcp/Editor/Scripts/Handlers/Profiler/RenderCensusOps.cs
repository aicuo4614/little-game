using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KabuUnityMcp.Editor.Handlers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// unity_get_render_census：场景渲染量级盘点（灯 / Renderer / 材质粗分），只读。
    /// </summary>
    internal static class RenderCensusOps
    {
        public const string Schema = "kabu.perf.render-census/v1";

        public static HandlerResult GetCensus()
        {
            var scene = SceneManager.GetActiveScene();

            var lights = UnityEngine.Object.FindObjectsByType<Light>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(l => l && l.gameObject.scene == scene)
                .ToArray();

            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(r => r && r.gameObject.scene == scene)
                .ToArray();

            var realtime = 0;
            var mixed = 0;
            var baked = 0;
            var shadowsOn = 0;
            var byType = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var l in lights)
            {
                var key = l.type.ToString();
                if (!byType.ContainsKey(key)) byType[key] = 0;
                byType[key]++;

                switch (l.lightmapBakeType)
                {
                    case LightmapBakeType.Realtime: realtime++; break;
                    case LightmapBakeType.Mixed: mixed++; break;
                    case LightmapBakeType.Baked: baked++; break;
                }

                if (l.shadows != LightShadows.None && l.enabled && l.gameObject.activeInHierarchy)
                    shadowsOn++;
            }

            var meshR = 0;
            var skinned = 0;
            var otherR = 0;
            var enabledR = 0;
            var uniqueMats = new HashSet<int>();
            foreach (var r in renderers)
            {
                if (r.enabled && r.gameObject.activeInHierarchy) enabledR++;
                if (r is MeshRenderer) meshR++;
                else if (r is SkinnedMeshRenderer) skinned++;
                else otherR++;

                var mats = r.sharedMaterials;
                if (mats == null) continue;
                foreach (var m in mats)
                {
                    if (m != null) uniqueMats.Add(m.GetInstanceID());
                }
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
                    lights = new
                    {
                        total = lights.Length,
                        realtime,
                        mixed,
                        baked,
                        shadowCastingEnabled = shadowsOn,
                        byType = byType.Select(kv => new { type = kv.Key, count = kv.Value }).ToList()
                    },
                    renderers = new
                    {
                        total = renderers.Length,
                        enabledActive = enabledR,
                        meshRenderer = meshR,
                        skinnedMeshRenderer = skinned,
                        other = otherR,
                        uniqueMaterials = uniqueMats.Count
                    }
                }
            });
        }
    }
}
