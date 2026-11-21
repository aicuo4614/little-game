using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// ProfilerRecorder 计数器：list 发现名称；get 需等 1～2 编辑器帧后再读。
    /// </summary>
    internal static class ProfilerCounterOps
    {
        private static readonly string[] ValidCategories =
        {
            "Render", "Scripts", "Memory", "Physics",
#if UNITY_2022_2_OR_NEWER
            "Physics2D",
#endif
            "Animation", "Audio", "Lighting", "Network", "Gui", "UI", "Ai", "Video",
            "Loading", "Input", "Vr", "Internal", "Particles", "FileIO", "VirtualTexturing"
        };

        public static HandlerResult List(string categoryName, int offset, int limit, string nameContains)
        {
            if (!TryResolveCategory(categoryName, out var category, out var error))
                return HandlerResult.Fail(error, -32602);

            offset = Math.Max(0, offset);
            limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);

            var names = DiscoverCounterNames(category);
            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                var filter = nameContains.Trim();
                names = names
                    .Where(n => n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            var total = names.Count;
            var page = names.Skip(offset).Take(limit).ToList();
            var next = offset + page.Count;
            return HandlerResult.Success(new
            {
                message = $"category '{category.Name}' 共 {total} 个计数器。",
                category = category.Name,
                total,
                returned = page.Count,
                offset,
                limit,
                nextOffset = next < total ? (int?)next : null,
                counters = page
            });
        }

        /// <summary>启动 recorders；调用方等帧后 <see cref="ReadAndDispose"/>。</summary>
        public static bool TryBeginCapture(
            string categoryName,
            IList<string> requestedNames,
            out List<ProfilerRecorder> recorders,
            out List<string> names,
            out string error)
        {
            recorders = null;
            names = null;
            error = null;

            if (!TryResolveCategory(categoryName, out var category, out error))
                return false;

            if (requestedNames == null || requestedNames.Count == 0)
            {
                names = DiscoverCounterNames(category);
                if (names.Count == 0)
                {
                    error = $"category '{category.Name}' 下没有可用计数器。";
                    return false;
                }

                // 避免一次拉爆：未指定时最多 40 个
                if (names.Count > 40)
                    names = names.Take(40).ToList();
            }
            else
            {
                names = requestedNames
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (names.Count == 0)
                {
                    error = "counters 为空。请提供名称，或省略以读取该 category 前 40 个。";
                    return false;
                }

                if (names.Count > 80)
                {
                    error = "一次最多读取 80 个 counters。请缩小列表或分批调用。";
                    return false;
                }
            }

            recorders = new List<ProfilerRecorder>(names.Count);
            try
            {
                foreach (var name in names)
                    recorders.Add(ProfilerRecorder.StartNew(category, name));
            }
            catch (Exception ex)
            {
                DisposeAll(recorders);
                recorders = null;
                names = null;
                error = $"启动 ProfilerRecorder 失败：{ex.Message}";
                return false;
            }

            return true;
        }

        public static HandlerResult ReadAndDispose(string categoryName, List<ProfilerRecorder> recorders, List<string> names)
        {
            if (recorders == null || names == null || recorders.Count != names.Count)
            {
                DisposeAll(recorders);
                return HandlerResult.Fail("内部错误：recorder 与名称列表不匹配。");
            }

            var data = new List<object>(names.Count);
            try
            {
                for (var i = 0; i < recorders.Count; i++)
                {
                    var recorder = recorders[i];
                    var name = names[i];
                    long value = 0;
                    var valid = recorder.Valid;
                    if (valid)
                    {
                        var current = recorder.CurrentValue;
                        if (current != 0)
                            value = current;
                        else if (recorder.Count > 0)
                            value = recorder.GetSample(recorder.Count - 1).Value;
                        else
                            value = current;
                    }

                    data.Add(new
                    {
                        name,
                        value,
                        valid,
                        unit = valid ? recorder.UnitType.ToString() : "Unknown"
                    });
                }
            }
            finally
            {
                DisposeAll(recorders);
            }

            return HandlerResult.Success(new
            {
                message = $"已捕获 {data.Count} 个计数器（category={categoryName}）。",
                category = categoryName,
                returned = data.Count,
                counters = data
            });
        }

        public static void DisposeAll(List<ProfilerRecorder> recorders)
        {
            if (recorders == null)
                return;
            foreach (var r in recorders)
            {
                try
                {
                    r.Dispose();
                }
                catch
                {
                    // ignore dispose races
                }
            }

            recorders.Clear();
        }

        private static List<string> DiscoverCounterNames(ProfilerCategory category)
        {
            var handles = new List<ProfilerRecorderHandle>(256);
            ProfilerRecorderHandle.GetAvailable(handles);
            return handles
                .Select(ProfilerRecorderHandle.GetDescription)
                .Where(d => string.Equals(d.Category.Name, category.Name, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static bool TryResolveCategory(string name, out ProfilerCategory category, out string error)
        {
            category = default;
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = $"请提供 category。有效：{string.Join(", ", ValidCategories)}";
                return false;
            }

            switch (name.Trim().ToLowerInvariant())
            {
                case "render": category = ProfilerCategory.Render; return true;
                case "scripts": category = ProfilerCategory.Scripts; return true;
                case "memory": category = ProfilerCategory.Memory; return true;
                case "physics": category = ProfilerCategory.Physics; return true;
#if UNITY_2022_2_OR_NEWER
                case "physics2d": category = ProfilerCategory.Physics2D; return true;
#endif
                case "animation": category = ProfilerCategory.Animation; return true;
                case "audio": category = ProfilerCategory.Audio; return true;
                case "lighting": category = ProfilerCategory.Lighting; return true;
                case "network": category = ProfilerCategory.Network; return true;
                case "gui":
                case "ui": category = ProfilerCategory.Gui; return true;
                case "ai": category = ProfilerCategory.Ai; return true;
                case "video": category = ProfilerCategory.Video; return true;
                case "loading": category = ProfilerCategory.Loading; return true;
                case "input": category = ProfilerCategory.Input; return true;
                case "vr": category = ProfilerCategory.Vr; return true;
                case "internal": category = ProfilerCategory.Internal; return true;
                case "particles": category = ProfilerCategory.Particles; return true;
                case "fileio": category = ProfilerCategory.FileIO; return true;
                case "virtualtexturing": category = ProfilerCategory.VirtualTexturing; return true;
                default:
                    error = $"未知 category '{name}'。有效：{string.Join(", ", ValidCategories)}";
                    return false;
            }
        }
    }
}
