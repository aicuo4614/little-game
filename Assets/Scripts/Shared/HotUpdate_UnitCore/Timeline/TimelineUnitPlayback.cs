using System;
using System.Collections.Generic;

/// <summary>管理一次演出中所有单位片段，供宿主与动画播放器组合。</summary>
public sealed class TimelineUnitPlayback
{
    private Func<TimelineClipUnitConfigBase, TimelineUnitSubjectType, string, List<BattleUnitTimelineActor>> resolve;
    private Dictionary<TimelineClipUnitConfigBase, TimelineUnitClipPlayback> clips =
        new Dictionary<TimelineClipUnitConfigBase, TimelineUnitClipPlayback>();

    /// <summary>传入本次演出的真实单位解析器。</summary>
    public TimelineUnitPlayback(Func<TimelineClipUnitConfigBase, TimelineUnitSubjectType, string, List<BattleUnitTimelineActor>> resolve)
    {
        this.resolve = resolve;
    }

    /// <summary>采样指定单位片段，首次采样捕获进入姿态。</summary>
    public void Sample(TimelineClipUnitConfigBase config, float time, Func<BattleUnitTimelineActor, float> unitTime = null)
    {
        if (!clips.TryGetValue(config, out var playback))
        {
            playback = new TimelineUnitClipPlayback(config, (subject, key) => resolve(config, subject, key));
            clips.Add(config, playback);
        }
        var duration = config is TimelineClipTranslateConfig ? playback.MeasureDuration() : config.Duration;
        playback.Sample(time, duration, unitTime);
    }

    /// <summary>停止、取消或重新定位前统一恢复单位，清空进入快照。</summary>
    public void Reset()
    {
        foreach (var clip in clips.Values)
        {
            for (var i = 0; i < clip.States.Count; i++)
            {
                clip.States[i].Actor.Restore();
            }
        }
        clips.Clear();
    }

    /// <summary>取得共同播放器实际捕获的片段状态，供编辑器终点手柄使用。</summary>
    public IReadOnlyList<TimelineUnitClipState> GetStates(string clipKey)
    {
        foreach (var pair in clips)
        {
            if (pair.Key.ClipKey == clipKey)
            {
                return pair.Value.States;
            }
        }
        return Array.Empty<TimelineUnitClipState>();
    }
}
