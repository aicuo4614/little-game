using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LittleGame.Client.SkillTimeline
{
    public sealed class SkillTimelineValidationReport
    {
        public bool Valid => Errors.Count == 0;
        public int BindingCount;
        public int TimelineCount;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    /// <summary>加载、缓存、热重载并校验单位技能绑定表与技能 JSON。</summary>
    public sealed class SkillTimelineLibrary
    {
        public const string BindingsResourcePath = "SkillTimelines/UnitSkillBindings";
        public const string SkillsResourceFolder = "SkillTimelines/Skills";
        public const string BindingsAssetPath = "Assets/LittleGame/Resources/SkillTimelines/UnitSkillBindings.json";
        public const string SkillsAssetFolder = "Assets/LittleGame/Resources/SkillTimelines/Skills";

        private readonly Dictionary<string, SkillTimelineConfig> timelines = new Dictionary<string, SkillTimelineConfig>(StringComparer.Ordinal);
        private UnitSkillBindingsConfig bindings = new UnitSkillBindingsConfig();

        public IReadOnlyDictionary<string, SkillTimelineConfig> Timelines => timelines;
        public UnitSkillBindingsConfig Bindings => bindings;

        public void Reload()
        {
            timelines.Clear();
            bindings = new UnitSkillBindingsConfig();

            bool loadedFromDisk = false;
#if UNITY_EDITOR
            if (File.Exists(BindingsAssetPath) && Directory.Exists(SkillsAssetFolder))
            {
                loadedFromDisk = true;
                var bindText = File.ReadAllText(BindingsAssetPath);
                bindings = SkillTimelineJson.Deserialize<UnitSkillBindingsConfig>(bindText) ?? new UnitSkillBindingsConfig();
                foreach (var file in Directory.GetFiles(SkillsAssetFolder, "*.json"))
                {
                    var text = File.ReadAllText(file);
                    var cfg = SkillTimelineJson.Deserialize<SkillTimelineConfig>(text);
                    if (cfg != null && !string.IsNullOrWhiteSpace(cfg.TimelineId))
                        timelines[cfg.TimelineId] = cfg;
                }
            }
#endif
            if (!loadedFromDisk)
            {
                var bindAsset = Resources.Load<TextAsset>(BindingsResourcePath);
                if (bindAsset != null)
                    bindings = SkillTimelineJson.Deserialize<UnitSkillBindingsConfig>(bindAsset.text) ?? new UnitSkillBindingsConfig();
                foreach (var asset in Resources.LoadAll<TextAsset>(SkillsResourceFolder))
                {
                    if (asset == null) continue;
                    var cfg = SkillTimelineJson.Deserialize<SkillTimelineConfig>(asset.text);
                    if (cfg != null && !string.IsNullOrWhiteSpace(cfg.TimelineId))
                        timelines[cfg.TimelineId] = cfg;
                }
            }
        }

        public SkillTimelineConfig Get(string timelineId)
        {
            if (string.IsNullOrWhiteSpace(timelineId)) return null;
            return timelines.TryGetValue(timelineId, out var cfg) ? cfg : null;
        }

        /// <summary>按 (单位类型, 触发事件, 二星分支, 星级) 优先级匹配技能 JSON。</summary>
        public string ResolveTimelineId(string unitKind, string trigger, string branch = "", int star = 1)
        {
            if (bindings?.Bindings == null || bindings.Bindings.Count == 0) return null;
            UnitSkillBindingEntry best = null;
            int bestScore = -1;
            foreach (var b in bindings.Bindings)
            {
                if (b == null || string.IsNullOrWhiteSpace(b.TimelineId)) continue;
                bool unitMatch = string.Equals(b.UnitKind, unitKind, StringComparison.OrdinalIgnoreCase) || b.UnitKind == "*";
                bool triggerMatch = string.Equals(b.Trigger, trigger, StringComparison.OrdinalIgnoreCase);
                if (!unitMatch || !triggerMatch) continue;

                bool hasBranchFilter = !string.IsNullOrWhiteSpace(b.Branch);
                if (hasBranchFilter && !string.Equals(b.Branch, branch, StringComparison.OrdinalIgnoreCase)) continue;
                if (star < Math.Max(1, b.MinStar)) continue;

                int score = (b.UnitKind == "*" ? 0 : 100)
                          + (hasBranchFilter ? 50 : 0)
                          + Math.Max(1, b.MinStar) * 10;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            return best?.TimelineId;
        }

        public SkillTimelineValidationReport ValidateAll()
        {
            var report = new SkillTimelineValidationReport
            {
                BindingCount = bindings?.Bindings?.Count ?? 0,
                TimelineCount = timelines.Count
            };

            if (report.TimelineCount == 0)
                report.Errors.Add("未加载到任何技能时间轴 JSON");
            if (report.BindingCount == 0)
                report.Errors.Add("未加载到任何单位技能绑定记录");

            foreach (var kv in timelines)
            {
                try
                {
                    var schedule = new SkillTimelineSchedule(kv.Value);
                    if (schedule.TotalDuration <= 0f)
                        report.Warnings.Add($"技能 {kv.Key} 总时长为 0");
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"技能 {kv.Key} 校验失败: {ex.Message}");
                }
            }

            if (bindings?.Bindings != null)
            {
                for (int i = 0; i < bindings.Bindings.Count; i++)
                {
                    var b = bindings.Bindings[i];
                    if (b == null) { report.Errors.Add($"绑定索引 {i} 为空"); continue; }
                    if (string.IsNullOrWhiteSpace(b.UnitKind) || string.IsNullOrWhiteSpace(b.Trigger))
                        report.Errors.Add($"绑定索引 {i} 缺少 UnitKind 或 Trigger");
                    if (string.IsNullOrWhiteSpace(b.TimelineId) || !timelines.ContainsKey(b.TimelineId))
                        report.Errors.Add($"绑定 {b.UnitKind}/{b.Trigger}/{b.Branch} 引用的 TimelineId 不存在: {b.TimelineId}");
                }
            }

            return report;
        }
    }
}
