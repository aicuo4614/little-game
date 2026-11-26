using System;
using System.Collections.Generic;

/// <summary>按绝对时间驱动技能片段。预览与实战共用时间边界，角色和资源由调用方绑定。</summary>
public sealed class TimelinePlayer
{
    private List<(TimelineClipConfigBase Config, float Start, float End, int Index)> clips =
        new List<(TimelineClipConfigBase, float, float, int)>();
    private bool[] activeClips;
    private Action<TimelineClipConfigBase, float> sampleClip;
    private Action<TimelineClipConfigBase> exitClip;
    private Action<TimelineClipPlayHitConfig> onHit;
    private Action resetPlayback;
    private Func<TimelineClipConfigBase, float, bool, float> mapSampleTime;
    private List<float> boundaries = new List<float>();
    private bool[] instantClips;
    private float sampledTime = -0.0001f;

    /// <summary>当前时间轴时间（秒）。</summary>
    public float Time { get; private set; }

    /// <summary>最后一个片段的结束时间（秒）。</summary>
    public float Duration { get; }

    /// <summary>当前播放状态。</summary>
    public TimelinePlaybackStateType State { get; private set; }

    /// <summary>可选诊断回调，提供片段、是否进入及本次边界采样的时间（秒）。</summary>
    public event Action<TimelineClipConfigBase, bool, float> ClipBoundary;

    /// <summary>当前时刻的角色、特效与相机均采样完成后触发，用于依赖最终机位的视觉对齐。</summary>
    public event Action SampleCompleted;

    /// <summary>校验配置，并绑定片段采样、退出和命中回调。配置修改后重新创建播放器。</summary>
    /// <param name="config">本次演出的配置。</param>
    /// <param name="sampleClip">按片段内部秒数采样，同一时刻允许重复调用。</param>
    /// <param name="exitClip">释放当前片段持有的演出资源。</param>
    /// <param name="onHit">正向播放到达命中点时调用。预览可省略。</param>
    /// <param name="mapSampleTime">可选的表现时间映射。第三个参数为 true 时将局部时钟转换为全局时间，反之则从全局换算到局部时钟。</param>
    public TimelinePlayer(
        TimelineConfig config,
        Action<TimelineClipConfigBase, float> sampleClip,
        Action<TimelineClipConfigBase> exitClip,
        Action<TimelineClipPlayHitConfig> onHit = null, Action resetPlayback = null,
        Func<TimelineClipConfigBase, bool> supportsClip = null,
        Func<TimelineClipConfigBase, float, bool, float> mapSampleTime = null)
    {
        if (config?.Clips is not List<TimelineClipConfigBase> source || source.Count == 0)
        {
            throw new ArgumentException("Timeline must contain at least one clip.", nameof(config));
        }

        this.sampleClip = sampleClip ?? throw new ArgumentNullException(nameof(sampleClip));
        this.exitClip = exitClip ?? throw new ArgumentNullException(nameof(exitClip));
        this.onHit = onHit;
        this.resetPlayback = resetPlayback;
        this.mapSampleTime = mapSampleTime;

        var schedule = new TimelineSchedule(config);
        supportsClip ??= TimelinePlayback.Supports;
        var duration = 0f;
        for (var i = 0; i < source.Count; i++)
        {
            var clip = source[i];
            var binding = clip is TimelineClipSetCastTargetsConfig || clip is TimelineClipSetHitTargetsConfig;
            if (!supportsClip(clip))
            {
                throw new ArgumentException($"Timeline clip {i} ({clip.Type}, key={clip.ClipKey}) is not supported by the battle player.", nameof(config));
            }

            var start = schedule.GetStart(clip);
            var end = schedule.GetEnd(clip);
            if (!IsValidTime(clip.StartTime) || !IsValidTime(clip.DelayTime)
                || !IsValidTime(clip.Duration) || !IsValidTime(end)
                || end < start)
            {
                throw new ArgumentException($"Timeline clip {i} has an invalid time range.", nameof(config));
            }

            if (clip is TimelineClipPlaySpineConfig spine
                && (string.IsNullOrWhiteSpace(spine.AnimationName) || !IsValidTime(spine.Speed) || spine.Speed == 0f))
            {
                throw new ArgumentException($"Timeline clip {i} has an invalid Spine animation or speed.", nameof(config));
            }

            if (clip is TimelineClipPlayHitConfig hit && !IsValidTime(hit.SpineAnimationDelay))
            {
                throw new ArgumentException($"Timeline clip {i} has an invalid Spine hit animation delay.", nameof(config));
            }

            if (binding)
            {
                continue;
            }
            clips.Add((clip, start, end, i));
            boundaries.Add(MapSampleTime(clip, start, true));
            boundaries.Add(MapSampleTime(clip, end, true));
            duration = Math.Max(duration, end);
        }

        clips.Sort((left, right) =>
        {
            var order = left.Start.CompareTo(right.Start);
            return order == 0 ? left.Index.CompareTo(right.Index) : order;
        });
        activeClips = new bool[clips.Count];
        instantClips = new bool[clips.Count];
        boundaries.Sort();
        Duration = string.IsNullOrEmpty(config.DurationBindClipKey)
            ? duration : schedule.GetEnd(config.Clips.Find(clip => clip.ClipKey == config.DurationBindClipKey)
                ?? throw new ArgumentException("Timeline duration binding does not exist."));
    }

    /// <summary>从头播放或从暂停位置继续。重新开始时处理 0 秒命中点。</summary>
    public void Play()
    {
        switch (State)
        {
            case TimelinePlaybackStateType.Playing:
            {
                return;
            }
            case TimelinePlaybackStateType.Paused:
            {
                State = TimelinePlaybackStateType.Playing;
                return;
            }
            default:
            {
                ClearActiveClips(Time);
                ResetSampling();
                Time = 0f;
                State = TimelinePlaybackStateType.Playing;
                Sample(Time);
                DispatchHits(0f, 0f, true);
                break;
            }
        }
    }

    /// <summary>暂停推进，保留当前采样画面。</summary>
    public void Pause()
    {
        if (State == TimelinePlaybackStateType.Playing)
        {
            State = TimelinePlaybackStateType.Paused;
        }
    }

    /// <summary>向前推进指定秒数，按时间顺序发出跨过的命中通知。</summary>
    public void Tick(float deltaTime)
    {
        if (!IsValidTime(deltaTime))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        }

        if (State != TimelinePlaybackStateType.Playing || deltaTime == 0f)
        {
            return;
        }

        var previous = Time;
        var next = Math.Min(Time + deltaTime, Duration);
        DispatchHits(previous, next, false);
        if (State != TimelinePlaybackStateType.Playing)
        {
            return;
        }

        Time = next;
        Sample(Time);
        if (Time >= Duration)
        {
            State = TimelinePlaybackStateType.Completed;
        }
    }

    /// <summary>暂停并采样指定时间，先清理旧片段，再重建当前画面。拖动不触发命中。</summary>
    public void Seek(float time)
    {
        if (!IsValidTime(time))
        {
            throw new ArgumentOutOfRangeException(nameof(time));
        }

        State = TimelinePlaybackStateType.Paused;
        ClearActiveClips(Time);
        ResetSampling();
        Time = Math.Min(time, Duration);
        Sample(Time);
    }

    /// <summary>预览采样：向前沿用边界和实例，向后重建；始终不发送战斗命中回调。</summary>
    public void Evaluate(float time)
    {
        if (!IsValidTime(time))
        {
            throw new ArgumentOutOfRangeException(nameof(time));
        }
        if (time < Time)
        {
            Seek(time);
            return;
        }
        State = TimelinePlaybackStateType.Paused;
        Time = Math.Min(time, Duration);
        Sample(Time);
    }

    /// <summary>停止并释放正在显示的片段，时间归零。</summary>
    public void Stop()
    {
        State = TimelinePlaybackStateType.Stopped;
        var previousTime = Time;
        Time = 0f;
        ClearActiveClips(previousTime);
        ResetSampling();
    }

    private void DispatchHits(float previous, float next, bool includeStart)
    {
        for (var i = 0; i < clips.Count && State == TimelinePlaybackStateType.Playing; i++)
        {
            var clip = clips[i];
            if (clip.Start > next)
            {
                break;
            }

            if ((clip.Start > previous || includeStart && clip.Start == previous)
                && clip.Config is TimelineClipPlayHitConfig hit)
            {
                // 即使一帧跨过整个片段，也先在命中点采样，再通知战斗侧。
                if (Time != clip.Start)
                {
                    Time = clip.Start;
                    Sample(Time);
                }

                onHit?.Invoke(hit);
            }
        }
    }

    private void Sample(float time)
    {
        // 逐个跨过进入/退出边界，确保大步进、连续片段与瞬时朝向都获得准确终态。
        for (var i = 0; i < boundaries.Count; i++)
        {
            var boundary = boundaries[i];
            if (boundary > sampledTime && boundary <= time)
            {
                SampleAt(boundary);
                sampledTime = boundary;
            }
        }
        if (sampledTime != time)
        {
            SampleAt(time);
            sampledTime = time;
        }
    }

    private void SampleAt(float time)
    {
        // 同一时刻先更新角色，再让相机捕获进入快照和跟随挂点。
        SamplePhase(time, false);
        SamplePhase(time, true);
        SampleCompleted?.Invoke();
    }

    private void SamplePhase(float time, bool cameraPhase)
    {
        // 同类片段先释放离开的，再采样进入的，避免旧状态覆盖新状态。
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var sampleTime = MapSampleTime(clip.Config, time, false);
            if (IsCamera(clip.Config) == cameraPhase && activeClips[i] && (sampleTime < clip.Start || sampleTime >= clip.End))
            {
                sampleClip(clip.Config, clip.End - clip.Start);
                activeClips[i] = false;
                exitClip(clip.Config);
                ClipBoundary?.Invoke(clip.Config, false, time);
            }
        }

        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            if (IsCamera(clip.Config) != cameraPhase)
            {
                continue;
            }
            var sampleTime = MapSampleTime(clip.Config, time, false);
            if (clip.Start == clip.End && sampleTime >= clip.Start && !instantClips[i])
            {
                instantClips[i] = true;
                ClipBoundary?.Invoke(clip.Config, true, time);
                sampleClip(clip.Config, 0f);
                exitClip(clip.Config);
                ClipBoundary?.Invoke(clip.Config, false, time);
                continue;
            }
            if (sampleTime >= clip.Start && sampleTime < clip.End)
            {
                var entering = !activeClips[i];
                activeClips[i] = true;
                if (entering)
                {
                    ClipBoundary?.Invoke(clip.Config, true, time);
                }
                sampleClip(clip.Config, sampleTime - clip.Start);
            }
        }
    }

    private float MapSampleTime(TimelineClipConfigBase clip, float time, bool toGlobal)
        => mapSampleTime?.Invoke(clip, time, toGlobal) ?? time;

    private static bool IsCamera(TimelineClipConfigBase clip)
        => clip is TimelineClipCameraShotConfig or TimelineClipCameraShotBlendConfig;

    private void ResetSampling()
    {
        sampledTime = -0.0001f;
        Array.Clear(instantClips, 0, instantClips.Length);
        resetPlayback?.Invoke();
    }

    private void ClearActiveClips(float time)
    {
        for (var i = clips.Count - 1; i >= 0; i--)
        {
            if (activeClips[i])
            {
                activeClips[i] = false;
                exitClip(clips[i].Config);
                ClipBoundary?.Invoke(clips[i].Config, false, time);
            }
        }
    }

    private static bool IsValidTime(float time)
    {
        return time >= 0f && !float.IsInfinity(time);
    }
}
