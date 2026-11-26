using System;
using System.Collections.Generic;

/// <summary>解析自定义事件连接。事件时间使用动画原始秒数，播放速度只参与解析。</summary>
public sealed class TimelineSchedule
{
    private Dictionary<string, TimelineClipConfigBase> clips = new Dictionary<string, TimelineClipConfigBase>();
    private Dictionary<string, (TimelineClipConfigBase Clip, TimelineEventConfigBase Event)> events =
        new Dictionary<string, (TimelineClipConfigBase, TimelineEventConfigBase)>();
    private Dictionary<TimelineClipConfigBase, float> starts = new Dictionary<TimelineClipConfigBase, float>();
    private HashSet<TimelineClipConfigBase> visiting = new HashSet<TimelineClipConfigBase>();
    private Dictionary<TimelineClipConfigBase, float> ends = new Dictionary<TimelineClipConfigBase, float>();
    private HashSet<TimelineClipConfigBase> visitingEnds = new HashSet<TimelineClipConfigBase>();

    /// <summary>建立片段、事件索引并检查连接、时间与循环依赖。</summary>
    public TimelineSchedule(TimelineConfig config)
    {
        foreach (var clip in config.Clips)
        {
            if (clip is null || string.IsNullOrEmpty(clip.ClipKey) || clips.ContainsKey(clip.ClipKey))
            {
                throw new ArgumentException("Clip keys must be present and unique.");
            }
            clips.Add(clip.ClipKey, clip);
            if (clip.EventConfigs is not List<TimelineEventConfigBase> points)
            {
                continue;
            }
            foreach (var point in points)
            {
                if (point is null || string.IsNullOrEmpty(point.EventKey) || events.ContainsKey(point.EventKey))
                {
                    throw new ArgumentException("Event keys must be present and unique.");
                }
                if (point.Time < 0f
                    || float.IsNaN(point.Time) || float.IsInfinity(point.Time))
                {
                    throw new ArgumentException("Events must use finite nonnegative time.");
                }
                events.Add(point.EventKey, (clip, point));
            }
        }
        foreach (var key in events.Keys)
        {
            if (clips.ContainsKey(key))
            {
                throw new ArgumentException("Clip and event keys must be distinct.");
            }
        }
        foreach (var clip in config.Clips)
        {
            GetStart(clip);
            GetEnd(clip);
            if (clip is TimelineClipUnitConfigBase unit)
            {
                if (!Enum.IsDefined(typeof(TimelineUnitSubjectType), unit.Subject))
                {
                    throw new ArgumentException("Unknown unit subject: " + unit.ClipKey);
                }
                TimelineUnitClipPlayback.Validate(unit);
                if (unit.Subject != TimelineUnitSubjectType.Caster)
                {
                    ValidateBinding(unit.SubjectBindingKey, unit.Subject != TimelineUnitSubjectType.CastTargets);
                }
                if (unit is TimelineClipTranslateConfig move)
                {
                    if (move.Destination <= TimelineMoveDestinationType.EffectTargets)
                    {
                        ValidateBinding(move.DestinationBindingKey, move.Destination != TimelineMoveDestinationType.CastTargets);
                    }
                }
                if (unit is TimelineClipRoleRotateToTargetConfig face)
                {
                    if (face.Destination > TimelineMoveDestinationType.EffectTargets || (int)face.Destination < 0)
                    {
                        throw new ArgumentException("Facing requires a reference unit.");
                    }
                    ValidateBinding(face.DestinationBindingKey, face.Destination != TimelineMoveDestinationType.CastTargets);
                }
            }
            if (clip is TimelineClipHitStopConfig stop)
            {
                if (!Enum.IsDefined(typeof(TimelineUnitSubjectType), stop.Subject))
                {
                    throw new ArgumentException("Unknown HitStop subject: " + stop.ClipKey);
                }
                if (stop.Subject != TimelineUnitSubjectType.Caster)
                {
                    ValidateBinding(stop.TargetBindingKey, stop.Subject != TimelineUnitSubjectType.CastTargets);
                }
            }
            if (clip is TimelineClipVisibilityConfig visibility)
            {
                TimelineVisibilityPlayback.Validate(visibility);
                ValidateVisibilityBinding(visibility, visibility.TargetBindingKey, false);
                ValidateVisibilityBinding(visibility, visibility.EffectBindingKey, true);
            }
            if (!string.IsNullOrEmpty(clip.TargetBindingKey))
            {
                if (!clips.TryGetValue(clip.TargetBindingKey, out var binding)
                    || (binding is not TimelineClipSetCastTargetsConfig && binding is not TimelineClipSetHitTargetsConfig))
                {
                    throw new ArgumentException("Object binding clip does not exist: " + clip.TargetBindingKey);
                }
                if (clip is TimelineClipPlayBulletConfig && binding is not TimelineClipSetHitTargetsConfig)
                {
                    throw new ArgumentException("PlayBullet requires an effect source/targets binding.");
                }
                if (clip is TimelineClipPlayHitConfig && binding is not TimelineClipSetHitTargetsConfig)
                {
                    throw new ArgumentException("PlayHit requires an effect source/targets binding.");
                }
            }
        }
        foreach (var pair in events.Values)
        {
            if (GetEventOffset(pair.Clip, pair.Event) > pair.Clip.Duration + 0.0001f)
            {
                throw new ArgumentException("Event is outside its clip.");
            }
        }
    }

    private void ValidateVisibilityBinding(TimelineClipVisibilityConfig visibility, string key, bool effect)
    {
        ValidateBinding(key, effect);
        if (!string.IsNullOrEmpty(key) && GetStart(clips[key]) > GetStart(visibility) + 0.0001f)
        {
            throw new ArgumentException($"Visibility binding starts later than the clip: {visibility.ClipKey}, binding={key}");
        }
    }

    /// <summary>获取片段实际起点。关联片段只使用事件时间加自身延迟。</summary>
    public float GetStart(TimelineClipConfigBase clip)
    {
        if (starts.TryGetValue(clip, out var start))
        {
            return start;
        }
        if (!visiting.Add(clip))
        {
            throw new ArgumentException("Timeline contains a cyclic event connection.");
        }
        start = clip.StartTime + clip.DelayTime;
        if (!string.IsNullOrEmpty(clip.PreKey))
        {
            if (clips.TryGetValue(clip.PreKey, out var previous))
            {
                start = GetEnd(previous) + clip.DelayTime;
            }
            else if (events.TryGetValue(clip.PreKey, out var source))
            {
                start = GetStart(source.Clip) + GetEventOffset(source.Clip, source.Event) + clip.DelayTime;
            }
            else
            {
                throw new ArgumentException("Connected event does not exist: " + clip.PreKey);
            }
        }
        visiting.Remove(clip);
        starts.Add(clip, start);
        return start;
    }

    /// <summary>解析结束绑定；与起点共同检查循环依赖。</summary>
    public float GetEnd(TimelineClipConfigBase clip)
    {
        if (ends.TryGetValue(clip, out var end))
        {
            return end;
        }
        if (!visitingEnds.Add(clip))
        {
            throw new ArgumentException("Timeline contains a cyclic end binding.");
        }
        if (string.IsNullOrEmpty(clip.EndByOtherClipEnd))
        {
            end = GetStart(clip) + clip.Duration;
        }
        else
        {
            if (!clips.TryGetValue(clip.EndByOtherClipEnd, out var other))
            {
                throw new ArgumentException("Timeline end binding does not exist: " + clip.EndByOtherClipEnd);
            }
            end = GetEnd(other);
        }
        visitingEnds.Remove(clip);
        ends.Add(clip, end);
        return end;
    }

    private void ValidateBinding(string key, bool effect)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }
        if (!clips.TryGetValue(key, out var binding)
            || (effect ? binding is not TimelineClipSetHitTargetsConfig : binding is not TimelineClipSetCastTargetsConfig))
        {
            throw new ArgumentException("Unit binding has an incorrect type or is missing: " + key);
        }
    }

    /// <summary>把动画原始秒数换成片段播放秒数。</summary>
    public static float GetEventOffset(TimelineClipConfigBase clip, TimelineEventConfigBase point)
    {
        if (point.ValueType == ValueType.Percent)
        {
            return point.Time * clip.Duration;
        }
        if (clip is TimelineClipPlaySpineConfig spine)
        {
            if (!(spine.Speed > 0f) || float.IsInfinity(spine.Speed))
            {
                throw new ArgumentException("Spine speed must be finite and positive.");
            }
            return point.Time / spine.Speed;
        }
        return point.Time;
    }
}
