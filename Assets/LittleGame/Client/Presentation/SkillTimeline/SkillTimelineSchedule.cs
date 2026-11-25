using System;
using System.Collections.Generic;

namespace LittleGame.Client.SkillTimeline
{
    public sealed class SkillScheduledClip
    {
        public SkillClipConfig Config { get; }
        public float Start { get; }
        public float End { get; }
        public int Index { get; }
        public float ResolvedDuration => Math.Max(0f, End - Start);

        public SkillScheduledClip(SkillClipConfig config, float start, float end, int index)
        {
            Config = config;
            Start = start;
            End = end;
            Index = index;
        }
    }

    /// <summary>解析技能时间轴片段起止时间与事件连接，检查唯一性、合法时间与循环依赖。</summary>
    public sealed class SkillTimelineSchedule
    {
        private readonly Dictionary<string, SkillClipConfig> clips = new Dictionary<string, SkillClipConfig>(StringComparer.Ordinal);
        private readonly Dictionary<string, (SkillClipConfig Clip, SkillEventConfig Event)> events =
            new Dictionary<string, (SkillClipConfig, SkillEventConfig)>(StringComparer.Ordinal);
        private readonly Dictionary<SkillClipConfig, float> starts = new Dictionary<SkillClipConfig, float>();
        private readonly Dictionary<SkillClipConfig, float> ends = new Dictionary<SkillClipConfig, float>();
        private readonly HashSet<SkillClipConfig> visitingStarts = new HashSet<SkillClipConfig>();
        private readonly HashSet<SkillClipConfig> visitingEnds = new HashSet<SkillClipConfig>();

        public List<SkillScheduledClip> OrderedClips { get; } = new List<SkillScheduledClip>();
        public float TotalDuration { get; }

        public SkillTimelineSchedule(SkillTimelineConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.TimelineId)) throw new ArgumentException("TimelineId 不能为空");
            if (config.Clips == null || config.Clips.Count == 0) throw new ArgumentException("技能时间轴至少包含一个片段");

            for (int i = 0; i < config.Clips.Count; i++)
            {
                var clip = config.Clips[i];
                if (clip == null || string.IsNullOrWhiteSpace(clip.ClipKey) || clips.ContainsKey(clip.ClipKey))
                    throw new ArgumentException($"片段 {i} 的 ClipKey 为空或重复");
                if (!IsValidTime(clip.StartTime) || !IsValidTime(clip.DelayTime) || !IsValidTime(clip.Duration))
                    throw new ArgumentException($"片段 {clip.ClipKey} 包含非法时间值");
                if (clip.Type == SkillClipType.PlayAnimation && (string.IsNullOrWhiteSpace(clip.ActionName) || !IsValidPositive(clip.Speed)))
                    throw new ArgumentException($"动作片段 {clip.ClipKey} 的 ActionName 或 Speed 非法");

                clips.Add(clip.ClipKey, clip);
                if (clip.EventConfigs == null) continue;
                foreach (var ev in clip.EventConfigs)
                {
                    if (ev == null || string.IsNullOrWhiteSpace(ev.EventKey) || events.ContainsKey(ev.EventKey))
                        throw new ArgumentException($"片段 {clip.ClipKey} 的 EventKey 为空或重复");
                    if (!IsValidTime(ev.Time))
                        throw new ArgumentException($"事件 {ev.EventKey} 时间必须为非负有限值");
                    events.Add(ev.EventKey, (clip, ev));
                }
            }

            foreach (var key in events.Keys)
            {
                if (clips.ContainsKey(key))
                    throw new ArgumentException($"EventKey 与 ClipKey 冲突: {key}");
            }

            float maxEnd = 0f;
            for (int i = 0; i < config.Clips.Count; i++)
            {
                var clip = config.Clips[i];
                float start = GetStart(clip);
                float end = GetEnd(clip);
                if (!IsValidTime(start) || !IsValidTime(end) || end < start)
                    throw new ArgumentException($"片段 {clip.ClipKey} 解析后的时间区间非法 [{start}, {end}]");
                OrderedClips.Add(new SkillScheduledClip(clip, start, end, i));
                if (end > maxEnd) maxEnd = end;
            }

            foreach (var pair in events.Values)
            {
                float offset = GetEventOffset(pair.Clip, pair.Event);
                if (offset > pair.Clip.Duration + 0.0001f)
                    throw new ArgumentException($"事件 {pair.Event.EventKey} 超出片段 {pair.Clip.ClipKey} 时长");
            }

            OrderedClips.Sort((a, b) =>
            {
                int cmp = a.Start.CompareTo(b.Start);
                return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
            });

            if (!string.IsNullOrWhiteSpace(config.DurationBindClipKey))
            {
                if (!clips.TryGetValue(config.DurationBindClipKey, out var bound))
                    throw new ArgumentException($"DurationBindClipKey 不存在: {config.DurationBindClipKey}");
                TotalDuration = GetEnd(bound);
            }
            else
            {
                TotalDuration = maxEnd;
            }
            BuildDefinition(config);
        }

        /// <summary>规则节点绑定片段起点或事件；动作结束节点绑定片段终点。</summary>
        public float ResolveKey(string key, bool end = false)
        {
            if (clips.TryGetValue(key ?? "", out var clip)) return end ? GetEnd(clip) : GetStart(clip);
            if (events.TryGetValue(key ?? "", out var ev)) return GetStart(ev.Clip) + GetEventOffset(ev.Clip, ev.Event);
            throw new ArgumentException("技能规则节点不存在: " + key);
        }

        public LittleGame.Combat.BattleSkillDefinition BuildDefinition(SkillTimelineConfig config)
        {
            if (config.Execution == null || !config.Execution.Enabled) return null;
            var e = config.Execution;
            var d = new LittleGame.Combat.BattleSkillDefinition {
                UnitKind = "*", Trigger = "Skill", TimelineId = config.TimelineId,
                LaunchAt = ResolveKey(e.LaunchKey), ImpactAt = ResolveKey(e.ImpactKey),
                RecoveryEnd = ResolveKey(e.RecoveryKey, true), InterruptOnControl = e.InterruptOnControl, TrackTarget = e.TrackTarget
            };
            d.Validate();
            if (d.RecoveryEnd > TotalDuration + .0001f) throw new ArgumentException("动作结束节点超出时间轴总时长");
            return d;
        }

        public float GetStart(SkillClipConfig clip)
        {
            if (starts.TryGetValue(clip, out float cached)) return cached;
            if (!visitingStarts.Add(clip))
                throw new ArgumentException($"检测到循环前置依赖: {clip.ClipKey}");

            float start = clip.StartTime + clip.DelayTime;
            if (!string.IsNullOrWhiteSpace(clip.PreKey))
            {
                if (clips.TryGetValue(clip.PreKey, out var prevClip))
                {
                    start = GetEnd(prevClip) + clip.DelayTime;
                }
                else if (events.TryGetValue(clip.PreKey, out var prevEvent))
                {
                    start = GetStart(prevEvent.Clip) + GetEventOffset(prevEvent.Clip, prevEvent.Event) + clip.DelayTime;
                }
                else
                {
                    throw new ArgumentException($"前置连接不存在: {clip.PreKey}");
                }
            }

            visitingStarts.Remove(clip);
            starts[clip] = start;
            return start;
        }

        public float GetEnd(SkillClipConfig clip)
        {
            if (ends.TryGetValue(clip, out float cached)) return cached;
            if (!visitingEnds.Add(clip))
                throw new ArgumentException($"检测到循环结束依赖: {clip.ClipKey}");

            float end;
            if (string.IsNullOrWhiteSpace(clip.EndByClipKey))
            {
                end = GetStart(clip) + clip.Duration;
            }
            else
            {
                if (!clips.TryGetValue(clip.EndByClipKey, out var target))
                    throw new ArgumentException($"结束绑定片段不存在: {clip.EndByClipKey}");
                end = GetEnd(target);
            }

            visitingEnds.Remove(clip);
            ends[clip] = end;
            return end;
        }

        private static float GetEventOffset(SkillClipConfig clip, SkillEventConfig ev)
        {
            if (clip.Type == SkillClipType.PlayAnimation && clip.Speed > 0.0001f)
                return ev.Time / clip.Speed;
            return ev.Time;
        }

        private static bool IsValidTime(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f;
        private static bool IsValidPositive(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v > 0f;
    }
}
