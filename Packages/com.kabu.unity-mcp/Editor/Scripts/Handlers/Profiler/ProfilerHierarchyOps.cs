using System;
using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// CPU Hierarchy：Top N 热点 + 按 itemId 展开子节点。
    /// 禁止整树 dump；遍历有节点上限。
    /// </summary>
    internal static class ProfilerHierarchyOps
    {
        private const int MaxVisitNodes = 8000;

        public static HandlerResult GetTop(
            int? frameIndex,
            int threadIndex,
            int topN,
            string sortBy,
            string nameContains,
            double? minMs,
            int? maxDepth,
            bool excludeEditorLoop = false,
            bool preferPlayerLoop = false)
        {
            if (!TryResolveFrame(frameIndex, out var frame, out var error))
                return HandlerResult.Fail(error);

            topN = Math.Clamp(topN <= 0 ? 20 : topN, 1, 100);
            threadIndex = Math.Max(0, threadIndex);
            var sort = ParseSort(sortBy);
            var depthLimit = maxDepth.HasValue ? Math.Max(1, maxDepth.Value) : 64;

            var sortColumn = sort == SortKind.GcAlloc
                ? HierarchyFrameDataView.columnGcMemory
                : sort == SortKind.TotalMs
                    ? HierarchyFrameDataView.columnTotalTime
                    : HierarchyFrameDataView.columnSelfTime;

            try
            {
                using (var view = ProfilerDriver.GetHierarchyFrameDataView(
                           frame,
                           threadIndex,
                           ResolveViewModes(),
                           sortColumn,
                           false))
                {
                    if (view == null || !view.valid)
                    {
                        return HandlerResult.Fail(
                            $"帧 {frame} threadIndex={threadIndex} 的 Hierarchy 无效。" +
                            "请先 unity_profiler_enable，并在 Play Mode 跑若干帧后再取。");
                    }

                    var items = new List<HierarchyItem>(256);
                    var visited = 0;
                    var collectRoot = view.GetRootItemID();
                    var usedPlayerLoop = false;
                    if (preferPlayerLoop)
                    {
                        var roots = new List<int>(16);
                        view.GetItemChildren(collectRoot, roots);
                        foreach (var id in roots)
                        {
                            var n = view.GetItemName(id) ?? "";
                            if (string.Equals(n, "PlayerLoop", StringComparison.Ordinal))
                            {
                                collectRoot = id;
                                usedPlayerLoop = true;
                                break;
                            }
                        }
                    }

                    Collect(
                        view,
                        collectRoot,
                        depth: 0,
                        depthLimit,
                        nameContains,
                        minMs,
                        excludeEditorLoop,
                        items,
                        ref visited);

                    items.Sort((a, b) =>
                    {
                        var cmp = sort switch
                        {
                            SortKind.TotalMs => b.TotalMs.CompareTo(a.TotalMs),
                            SortKind.GcAlloc => b.GcAlloc.CompareTo(a.GcAlloc),
                            _ => b.SelfMs.CompareTo(a.SelfMs)
                        };
                        return cmp != 0 ? cmp : string.CompareOrdinal(a.Name, b.Name);
                    });

                    if (items.Count > topN)
                        items.RemoveRange(topN, items.Count - topN);

                    var payload = new List<object>(items.Count);
                    foreach (var it in items)
                    {
                        payload.Add(new
                        {
                            id = it.Id,
                            name = it.Name,
                            depth = it.Depth,
                            totalMs = Round3(it.TotalMs),
                            selfMs = Round3(it.SelfMs),
                            gcAlloc = it.GcAlloc,
                            calls = it.Calls,
                            hasChildren = it.HasChildren
                        });
                    }

                    return HandlerResult.Success(new
                    {
                        message =
                            $"帧 {frame} 热点 Top {payload.Count}（sortBy={sort}，visited={visited}" +
                            (usedPlayerLoop ? "，root=PlayerLoop" : "") +
                            (excludeEditorLoop ? "，已过滤 EditorLoop/WaitForJobGroupID" : "") + "）。",
                        frameIndex = frame,
                        threadIndex,
                        threadName = view.threadName,
                        frameTimeMs = Round3(view.frameTimeMs),
                        frameGpuTimeMs = Round3(view.frameGpuTimeMs),
                        sortBy = sort.ToString(),
                        usedPlayerLoop,
                        excludeEditorLoop,
                        visited,
                        visitCapped = visited >= MaxVisitNodes,
                        returned = payload.Count,
                        items = payload
                    });
                }
            }
            catch (Exception ex)
            {
                return HandlerResult.Fail($"读取 Hierarchy 失败：{ex.Message}");
            }
        }

        public static HandlerResult GetChildren(
            int? frameIndex,
            int threadIndex,
            int itemId,
            int offset,
            int limit,
            string nameContains)
        {
            if (!TryResolveFrame(frameIndex, out var frame, out var error))
                return HandlerResult.Fail(error);

            threadIndex = Math.Max(0, threadIndex);
            offset = Math.Max(0, offset);
            limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);

            try
            {
                using (var view = ProfilerDriver.GetHierarchyFrameDataView(
                           frame,
                           threadIndex,
                           HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                           HierarchyFrameDataView.columnSelfTime,
                           false))
                {
                    if (view == null || !view.valid)
                    {
                        return HandlerResult.Fail(
                            $"帧 {frame} threadIndex={threadIndex} 的 Hierarchy 无效。请先 enable 并录制若干帧。");
                    }

                    var children = new List<int>(64);
                    view.GetItemChildren(itemId, children);

                    var filtered = new List<object>();
                    foreach (var childId in children)
                    {
                        var name = view.GetItemName(childId) ?? "";
                        if (!string.IsNullOrWhiteSpace(nameContains) &&
                            name.IndexOf(nameContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        filtered.Add(BuildItemPayload(view, childId));
                    }

                    var total = filtered.Count;
                    var page = new List<object>();
                    for (var i = offset; i < filtered.Count && page.Count < limit; i++)
                        page.Add(filtered[i]);

                    var next = offset + page.Count;
                    return HandlerResult.Success(new
                    {
                        message = $"itemId={itemId} 子节点 {total} 个。",
                        frameIndex = frame,
                        threadIndex,
                        parentId = itemId,
                        parentName = view.GetItemName(itemId),
                        total,
                        returned = page.Count,
                        offset,
                        limit,
                        nextOffset = next < total ? (int?)next : null,
                        children = page
                    });
                }
            }
            catch (Exception ex)
            {
                return HandlerResult.Fail($"读取 Hierarchy 子节点失败：{ex.Message}");
            }
        }

        private static void Collect(
            HierarchyFrameDataView view,
            int parentId,
            int depth,
            int depthLimit,
            string nameContains,
            double? minMs,
            bool excludeEditorLoop,
            List<HierarchyItem> sink,
            ref int visited)
        {
            if (visited >= MaxVisitNodes || depth >= depthLimit)
                return;

            var children = new List<int>(32);
            view.GetItemChildren(parentId, children);
            foreach (var id in children)
            {
                if (visited >= MaxVisitNodes)
                    break;

                visited++;
                var name = view.GetItemName(id) ?? "";
                var selfMs = view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnSelfTime);
                var totalMs = view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnTotalTime);
                var gc = (long)view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnGcMemory);
                var calls = (int)view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
                var itemDepth = view.GetItemDepth(id);
                var hasChildren = view.HasItemChildren(id);

                var isNoise = IsHierarchyNoiseName(name);
                var passName = string.IsNullOrWhiteSpace(nameContains) ||
                               name.IndexOf(nameContains.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
                var passMs = !minMs.HasValue || selfMs >= minMs.Value || totalMs >= minMs.Value;
                var passNoise = !excludeEditorLoop || !isNoise;

                // 跳过根占位；收集真实 sample（可选过滤 EditorLoop / WaitForJobGroupID）
                if (passName && passMs && passNoise && !string.IsNullOrEmpty(name))
                {
                    sink.Add(new HierarchyItem
                    {
                        Id = id,
                        Name = name,
                        Depth = itemDepth,
                        SelfMs = selfMs,
                        TotalMs = totalMs,
                        GcAlloc = gc,
                        Calls = calls,
                        HasChildren = hasChildren
                    });
                }

                // 噪声节点本身不进榜，但仍向下扫子节点（若有）
                if (hasChildren)
                {
                    Collect(view, id, depth + 1, depthLimit, nameContains, minMs, excludeEditorLoop, sink, ref visited);
                }
            }
        }

        private static object BuildItemPayload(HierarchyFrameDataView view, int id)
        {
            return new
            {
                id,
                name = view.GetItemName(id),
                depth = view.GetItemDepth(id),
                totalMs = Round3(view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnTotalTime)),
                selfMs = Round3(view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnSelfTime)),
                gcAlloc = (long)view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnGcMemory),
                calls = (int)view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls),
                hasChildren = view.HasItemChildren(id)
            };
        }

        private static bool TryResolveFrame(int? frameIndex, out int frame, out string error)
        {
            frame = 0;
            error = null;
            var first = ProfilerDriver.firstFrameIndex;
            var last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < 0 || last < first)
            {
                error = "Profiler 尚无录制帧。请 unity_profiler_enable 后在 Play Mode 跑若干帧再取 Hierarchy。";
                return false;
            }

            if (frameIndex.HasValue)
            {
                frame = frameIndex.Value;
                if (frame < first || frame > last)
                {
                    error = $"frameIndex={frame} 超出范围 [{first}, {last}]。";
                    return false;
                }

                return true;
            }

            frame = last;
            return true;
        }

        private static SortKind ParseSort(string sortBy)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return SortKind.SelfMs;
            switch (sortBy.Trim().ToLowerInvariant())
            {
                case "total":
                case "totalms":
                case "total_ms":
                    return SortKind.TotalMs;
                case "gc":
                case "gcalloc":
                case "gc_alloc":
                    return SortKind.GcAlloc;
                default:
                    return SortKind.SelfMs;
            }
        }

        private static double Round3(double v) => Math.Round(v, 3);

        /// <summary>Hierarchy TopN 噪声：Editor 根与 Job 等待 marker（self 高但不指导优化）。</summary>
        private static bool IsHierarchyNoiseName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.IndexOf("EditorLoop", StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("WaitForJobGroupID", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>合并同名 sample，并尽量隐藏仅 Editor 的样本。</summary>
        private static HierarchyFrameDataView.ViewModes ResolveViewModes()
        {
            return HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName
                   | HierarchyFrameDataView.ViewModes.HideEditorOnlySamples;
        }

        private enum SortKind
        {
            SelfMs,
            TotalMs,
            GcAlloc
        }

        private struct HierarchyItem
        {
            public int Id;
            public string Name;
            public int Depth;
            public double SelfMs;
            public double TotalMs;
            public long GcAlloc;
            public int Calls;
            public bool HasChildren;
        }
    }
}
