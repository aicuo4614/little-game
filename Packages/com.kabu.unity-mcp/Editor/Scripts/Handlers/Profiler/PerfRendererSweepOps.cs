using System;
using System.Collections.Generic;
using System.Linq;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// unity_perf_renderer_sweep 的纯组装：收集 Renderer、分组、摘要行。
    /// </summary>
    internal static class PerfRendererSweepOps
    {
        public const string Schema = "kabu.perf.renderer-sweep/v1";

        public static readonly string[] BasicsCounterNames =
        {
            "Batches Count",
            "Draw Calls Count",
            "SetPass Calls Count",
            "Triangles Count",
            "Vertices Count"
        };

        public sealed class TargetItem
        {
            public int InstanceId;
            public string Path;
            public string RendererType;
            public string GroupKey;
            public GameObject Go;
        }

        public sealed class BatchGroup
        {
            public int Index;
            public string Label;
            public List<TargetItem> Items = new List<TargetItem>();
        }

        public sealed class Metrics
        {
            public long Batches;
            public long DrawCalls;
            public long SetPassCalls;
            public long Triangles;
            public long Vertices;
        }

        public static List<TargetItem> CollectTargets(string rootPath, bool includeInactive, out string error)
        {
            error = null;
            var list = new List<TargetItem>();
            var seen = new HashSet<int>();

            Transform root = null;
            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                var go = GameObjectLookup.Find(null, rootPath.Trim(), null, out error);
                if (go == null)
                    return list;
                root = go.transform;
            }

            if (root != null)
            {
                CollectUnder(root, includeInactive, list, seen);
            }
            else
            {
                var scene = SceneManager.GetActiveScene();
                if (!scene.IsValid())
                {
                    error = "当前无有效活动场景。";
                    return list;
                }

                foreach (var rootGo in scene.GetRootGameObjects())
                    CollectUnder(rootGo.transform, includeInactive, list, seen);
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return list;
        }

        private static void CollectUnder(
            Transform root,
            bool includeInactive,
            List<TargetItem> list,
            HashSet<int> seen)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive);
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                var go = r.gameObject;
                if (!includeInactive && (!go.activeInHierarchy || !r.enabled))
                    continue;
                var id = go.GetInstanceID();
                if (!seen.Add(id))
                    continue;

                list.Add(new TargetItem
                {
                    InstanceId = id,
                    Path = GameObjectLookup.GetPath(go),
                    RendererType = r.GetType().Name,
                    Go = go
                });
            }
        }

        public static List<BatchGroup> BuildBatches(
            List<TargetItem> targets,
            string groupBy,
            string targetMode,
            int batchCount)
        {
            batchCount = Math.Clamp(batchCount <= 0 ? 10 : batchCount, 1, 50);
            groupBy = string.IsNullOrWhiteSpace(groupBy) ? "sibling" : groupBy.Trim();
            targetMode = string.IsNullOrWhiteSpace(targetMode) ? "rendererGo" : targetMode.Trim();

            // 先决定每个 item 的开关目标 GO（可能升到 parent）
            foreach (var t in targets)
            {
                var go = ResolveToggleGo(t.Go, targetMode);
                t.Go = go;
                t.InstanceId = go.GetInstanceID();
                t.Path = GameObjectLookup.GetPath(go);
            }

            // 去重：同一 GO 只保留一次
            var unique = new List<TargetItem>();
            var seen = new HashSet<int>();
            foreach (var t in targets)
            {
                if (!seen.Add(t.InstanceId))
                    continue;
                unique.Add(t);
            }

            Dictionary<string, List<TargetItem>> keyed;
            if (string.Equals(groupBy, "hierarchyChunk", StringComparison.OrdinalIgnoreCase))
            {
                keyed = new Dictionary<string, List<TargetItem>>();
                keyed["__all__"] = unique;
            }
            else if (string.Equals(groupBy, "parent", StringComparison.OrdinalIgnoreCase))
            {
                keyed = unique
                    .GroupBy(t => ParentKey(t.Go))
                    .ToDictionary(g => g.Key, g => g.ToList());
            }
            else
            {
                // sibling：同父节点一批
                keyed = unique
                    .GroupBy(t => ParentKey(t.Go))
                    .ToDictionary(g => g.Key, g => g.ToList());
            }

            var orderedKeys = keyed.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var rawGroups = orderedKeys.Select(k => keyed[k]).ToList();

            // hierarchyChunk：把扁平列表切成 batchCount 段
            if (string.Equals(groupBy, "hierarchyChunk", StringComparison.OrdinalIgnoreCase))
            {
                rawGroups = ChunkList(unique, batchCount);
            }
            else if (rawGroups.Count > batchCount)
            {
                rawGroups = MergeToCount(rawGroups, batchCount);
            }

            var batches = new List<BatchGroup>();
            for (var i = 0; i < rawGroups.Count; i++)
            {
                var items = rawGroups[i];
                var label = items.Count == 1
                    ? items[0].Path
                    : $"{items[0].Path} (+{items.Count - 1})";
                batches.Add(new BatchGroup
                {
                    Index = i,
                    Label = label,
                    Items = items
                });
            }

            return batches;
        }

        public static Metrics FromCounterMap(IReadOnlyDictionary<string, long> map)
        {
            long Get(string n) => map != null && map.TryGetValue(n, out var v) ? v : 0;
            return new Metrics
            {
                Batches = Get("Batches Count"),
                DrawCalls = Get("Draw Calls Count"),
                SetPassCalls = Get("SetPass Calls Count"),
                Triangles = Get("Triangles Count"),
                Vertices = Get("Vertices Count")
            };
        }

        public static object BuildRow(BatchGroup batch, Metrics baseline, Metrics after)
        {
            var drop = baseline.Batches - after.Batches;
            return new
            {
                batchIndex = batch.Index,
                label = batch.Label,
                objectCount = batch.Items.Count,
                paths = batch.Items.Select(i => i.Path).Take(20).ToArray(),
                before = new
                {
                    batches = baseline.Batches,
                    drawCalls = baseline.DrawCalls,
                    setPassCalls = baseline.SetPassCalls,
                    triangles = baseline.Triangles,
                    vertices = baseline.Vertices
                },
                after = new
                {
                    batches = after.Batches,
                    drawCalls = after.DrawCalls,
                    setPassCalls = after.SetPassCalls,
                    triangles = after.Triangles,
                    vertices = after.Vertices
                },
                delta = new
                {
                    batches = after.Batches - baseline.Batches,
                    drawCalls = after.DrawCalls - baseline.DrawCalls,
                    setPassCalls = after.SetPassCalls - baseline.SetPassCalls,
                    triangles = after.Triangles - baseline.Triangles,
                    vertices = after.Vertices - baseline.Vertices,
                    batchesDrop = drop
                }
            };
        }

        private static GameObject ResolveToggleGo(GameObject go, string targetMode)
        {
            if (go == null)
                return null;
            if (string.Equals(targetMode, "parent", StringComparison.OrdinalIgnoreCase)
                && go.transform.parent != null)
                return go.transform.parent.gameObject;
            return go;
        }

        private static string ParentKey(GameObject go)
        {
            if (go == null)
                return "<null>";
            if (go.transform.parent == null)
                return "<root>/" + go.name;
            return GameObjectLookup.GetPath(go.transform.parent.gameObject);
        }

        private static List<List<TargetItem>> ChunkList(List<TargetItem> items, int chunks)
        {
            var result = new List<List<TargetItem>>();
            if (items.Count == 0)
                return result;
            chunks = Math.Min(chunks, items.Count);
            var size = (int)Math.Ceiling(items.Count / (double)chunks);
            for (var i = 0; i < items.Count; i += size)
                result.Add(items.GetRange(i, Math.Min(size, items.Count - i)));
            return result;
        }

        private static List<List<TargetItem>> MergeToCount(List<List<TargetItem>> groups, int count)
        {
            if (groups.Count <= count)
                return groups;
            var result = new List<List<TargetItem>>();
            var size = (int)Math.Ceiling(groups.Count / (double)count);
            for (var i = 0; i < groups.Count; i += size)
            {
                var merged = new List<TargetItem>();
                for (var j = i; j < Math.Min(i + size, groups.Count); j++)
                    merged.AddRange(groups[j]);
                result.Add(merged);
            }
            return result;
        }
    }
}
