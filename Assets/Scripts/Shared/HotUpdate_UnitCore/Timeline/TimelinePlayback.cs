using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>技能预览和实战的共同播放入口（对齐 Demo TimelinePlayback，原生驱动 3D PlayAnimator + 全套 Clip）。</summary>
public sealed class TimelinePlayback
{
    private readonly TimelineConfig config;
    private readonly TimelinePlaybackContext context;
    private readonly TimelineSchedule schedule;
    private readonly TimelineUnitPlayback units;
    private readonly TimelineCameraPlayback camera;
    private readonly TimelineEnvironmentPlayback environment;
    private readonly TimelineVisibilityPlayback visibility;
    private readonly Dictionary<TimelineClipConfigBase, List<TimelineClipPlayback>> animations =
        new Dictionary<TimelineClipConfigBase, List<TimelineClipPlayback>>();
    private readonly Dictionary<TimelineClipConfigBase, List<TimelineEffectPlayback>> effects =
        new Dictionary<TimelineClipConfigBase, List<TimelineEffectPlayback>>();
    private readonly List<BattleUnitTimelineActor> effectSubject = new List<BattleUnitTimelineActor>();
    private readonly Dictionary<TimelineClipConfigBase, List<BattleUnitTimelineActor>> effectActors =
        new Dictionary<TimelineClipConfigBase, List<BattleUnitTimelineActor>>();
    private readonly Dictionary<TimelineClipConfigBase, List<TimelineBulletPlayback>> bullets =
        new Dictionary<TimelineClipConfigBase, List<TimelineBulletPlayback>>();
    private readonly List<TimelineClipHitStopUtil.FreezeWindow> windows = new List<TimelineClipHitStopUtil.FreezeWindow>();
    private readonly Dictionary<(TimelineClipConfigBase, TimelineUnitSubjectType, string), List<BattleUnitTimelineActor>> resolved =
        new Dictionary<(TimelineClipConfigBase, TimelineUnitSubjectType, string), List<BattleUnitTimelineActor>>();
    private readonly Dictionary<(TimelineClipConfigBase, BattleUnitTimelineActor), List<TimelineClipHitStopUtil.FreezeWindow>> freezes =
        new Dictionary<(TimelineClipConfigBase, BattleUnitTimelineActor), List<TimelineClipHitStopUtil.FreezeWindow>>();
    private readonly List<TimelineClipHitStopUtil.FreezeWindow> casterFreezeWindows;
    private readonly Dictionary<TimelineClipUnitConfigBase, Func<BattleUnitTimelineActor, float>> unitClocks =
        new Dictionary<TimelineClipUnitConfigBase, Func<BattleUnitTimelineActor, float>>();
    private float unitSampleTime;
    private readonly Vector3 cameraPosition;
    private readonly Quaternion cameraRotation;

    public TimelinePlayer Player { get; }
    public float? FirstImpactTime
    {
        get
        {
            foreach (var clip in config.Clips)
                if (clip is TimelineClipPlayHitConfig) return schedule.GetStart(clip);
            return null;
        }
    }
    public IReadOnlyList<TimelineUnitClipState> GetUnitStates(string clipKey) => units.GetStates(clipKey);
    public bool TryGetCameraPivot(string key, bool from, out Vector3 point) => camera.TryGetPivot(key, from, out point);

    public TimelinePlayback(
        TimelineConfig config,
        TimelinePlaybackContext context,
        Action<TimelineClipPlayHitConfig> onHit = null)
    {
        this.config = TimelineJsonUtil.FromJson(TimelineJsonUtil.ToJson(config));
        this.context = context;
        environment = new TimelineEnvironmentPlayback(context);
        PrepareDurations();
        schedule = new TimelineSchedule(this.config);
        units = new TimelineUnitPlayback(ResolveBinding);
        camera = new TimelineCameraPlayback(this.config, schedule, context);
        visibility = new TimelineVisibilityPlayback(context);
        TimelineCameraPlayback.Validate(this.config, schedule);
        if (context.ShakeTransform)
        {
            cameraPosition = context.ShakeTransform.localPosition;
            cameraRotation = context.ShakeTransform.localRotation;
        }

        var animClip = this.config.Clips.Find(clip => clip is TimelineClipPlayAnimatorConfig);
        if (animClip is not null && this.config.Clips.Exists(clip => clip is TimelineClipHitStopConfig))
        {
            var casters = Resolve(animClip, TimelineUnitSubjectType.Caster);
            if (casters.Count > 0)
            {
                casterFreezeWindows = GetFreezeWindows(null, casters[0]);
            }
        }

        Player = new TimelinePlayer(
            this.config,
            Sample,
            Exit,
            onHit,
            Reset,
            context.SampleEditorClip is not null ? _ => true : Supports,
            MapAnimatorTime);
        Player.SampleCompleted += AlignBulletVisuals;
    }

    private void PrepareDurations()
    {
        foreach (var clip in config.Clips)
        {
            if (clip is TimelineClipPlayAnimatorConfig anim)
            {
                var casters = context.Resolve(clip, TimelineUnitSubjectType.Caster, null);
                if (casters.Count > 0 && casters[0].Animator)
                {
                    var measured = TimelineAnimatorTrack.MeasureDuration(casters[0].Animator, anim.ParamName, anim.AnimatorStateSpeed);
                    if (measured > 0.0001f)
                    {
                        anim.Duration = measured;
                    }
                }
            }
            else if (clip is TimelineClipPlayBulletConfig bullet)
            {
                var targets = Resolve(clip, TimelineUnitSubjectType.EffectTargets);
                if (targets.Count == 0)
                {
                    continue;
                }

                var sources = Resolve(clip, TimelineUnitSubjectType.EffectSource);
                if (sources.Count == 0)
                {
                    continue;
                }

                var source = sources[0];
                var duration = 0f;
                foreach (var target in targets)
                {
                    var start = source.GetPoint(bullet.StartData, source.Root.position);
                    var end = target.GetPoint(bullet.EndData, source.Root.position);
                    duration = Mathf.Max(
                        duration,
                        Mathf.Max(0f, bullet.MoveDelayTime)
                        + Mathf.Max(
                            TimelineClipPlayBulletUtil.MinFlyDuration,
                            Vector3.Distance(start, end) / Mathf.Max(bullet.MoveSpeed, 0.01f)));
                }

                if (targets.Count > 0)
                {
                    bullet.Duration = duration;
                }
            }
        }
    }

    /// <summary>共享执行层支持的 3D 片段（对齐 Demo 并在实战原生支持 PlayAnimator）。</summary>
    public static bool Supports(TimelineClipConfigBase clip)
    {
        return clip is TimelineClipPlayAnimatorConfig or TimelineClipPlayHitConfig
            or TimelineClipPlayEffectConfig or TimelineClipPlayBulletConfig
            or TimelineClipCameraShakeConfig or TimelineClipHitStopConfig
            or TimelineClipCameraShotConfig or TimelineClipCameraShotBlendConfig or TimelineClipVisibilityConfig
            or TimelineClipSetCastTargetsConfig or TimelineClipSetHitTargetsConfig
            or TimelineClipTranslateConfig or TimelineClipRotateConfig
            or TimelineClipScaleConfig or TimelineClipRoleRotateToTargetConfig
            or TimelineClipHideBattleUIConfig or TimelineClipSceneDarkenConfig or TimelineClipSetSkyboxConfig;
    }

    public static void Validate(TimelineConfig config)
    {
        _ = new TimelinePlayer(config, (_, _) => { }, _ => { });
        TimelineCameraPlayback.Validate(config, new TimelineSchedule(config));
        foreach (var clip in config.Clips)
        {
            if (clip is TimelineClipPlayBulletConfig facing
                && (!Enum.IsDefined(typeof(TimelineBulletFacingType), facing.FacingType)
                    || float.IsNaN(facing.BillboardAngleOffset) || float.IsInfinity(facing.BillboardAngleOffset)))
            {
                throw new ArgumentException("Invalid bullet facing mode or angle: " + clip.ClipKey);
            }

            if (clip is TimelineClipPlayEffectConfig effect && effect.PointData is null
                || clip is TimelineClipPlayBulletConfig bullet && (bullet.StartData is null || bullet.EndData is null))
            {
                throw new ArgumentException("Effect/bullet attachment data is required: " + clip.ClipKey);
            }
        }
    }

    public static List<string> EffectPaths(TimelineConfig config)
    {
        var result = new List<string>();
        foreach (var clip in config.Clips)
        {
            var path = clip is TimelineClipPlayEffectConfig effect ? effect.EffectPath
                : clip is TimelineClipPlayBulletConfig bullet ? bullet.Path
                : (clip as TimelineClipPlayHitConfig)?.EffectPath;
            if (!string.IsNullOrEmpty(path) && !result.Contains(path))
            {
                result.Add(path);
            }
        }

        return result;
    }

    private List<BattleUnitTimelineActor> Resolve(TimelineClipConfigBase clip, TimelineUnitSubjectType subject)
        => ResolveBinding(clip, subject, clip.TargetBindingKey);

    private List<BattleUnitTimelineActor> ResolveBinding(TimelineClipConfigBase clip, TimelineUnitSubjectType subject, string key)
    {
        var identity = (clip, subject, key ?? string.Empty);
        if (!resolved.TryGetValue(identity, out var actors))
        {
            actors = context.Resolve(clip, subject, key);
            resolved.Add(identity, actors);
        }

        return actors;
    }

    private void Sample(TimelineClipConfigBase clip, float time)
    {
        if (clip is TimelineClipHideBattleUIConfig or TimelineClipSceneDarkenConfig or TimelineClipSetSkyboxConfig)
        {
            environment.Sample(clip, time);
            return;
        }

        if (clip is TimelineClipUnitConfigBase unit)
        {
            if (!unitClocks.TryGetValue(unit, out var clock))
            {
                clock = actor => LogicTime(clip, unitSampleTime, actor);
                unitClocks.Add(unit, clock);
            }

            unitSampleTime = time;
            units.Sample(unit, time, clock);
        }
        else if (clip is TimelineClipPlayAnimatorConfig || clip is TimelineClipPlayHitConfig)
        {
            SampleAnimation(clip, time);
        }
        else if (clip is TimelineClipPlayEffectConfig effect)
        {
            SampleEffect(effect, time);
        }
        else if (clip is TimelineClipPlayBulletConfig bullet)
        {
            SampleBullet(bullet, time);
        }
        else if (clip is TimelineClipCameraShotConfig or TimelineClipCameraShotBlendConfig)
        {
            camera.Sample(clip, time);
        }
        else if (clip is TimelineClipVisibilityConfig visibilityClip)
        {
            visibility.Enter(visibilityClip);
        }
        else if (clip is TimelineClipCameraShakeConfig shake)
        {
            SampleShake(shake, time);
        }
        else if (clip is not TimelineClipHitStopConfig)
        {
            context.SampleEditorClip?.Invoke(clip, time);
        }
    }

    private void SampleAnimation(TimelineClipConfigBase clip, float time)
    {
        if (!animations.TryGetValue(clip, out var players))
        {
            var actors = Resolve(clip, clip is TimelineClipPlayHitConfig ? TimelineUnitSubjectType.EffectTargets : TimelineUnitSubjectType.Caster);
            players = new List<TimelineClipPlayback>();
            var playingActors = new List<BattleUnitTimelineActor>();
            var effectSources = clip is TimelineClipPlayHitConfig ? Resolve(clip, TimelineUnitSubjectType.EffectSource) : null;
            var hitSource = effectSources is { Count: > 0 } ? effectSources[0] : null;
            for (var i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                if (clip is TimelineClipPlayHitConfig hit && !(context.CanPlayHit?.Invoke(hit, actor) ?? true))
                {
                    continue;
                }

                playingActors.Add(actor);
                players.Add(new TimelineClipPlayback(
                    actor.Animator,
                    actor.Animator,
                    actor.Root,
                    (path, parent) => context.SpawnEffect(path),
                    context.DespawnEffect,
                    actor,
                    context.EffectLight,
                    clip is TimelineClipPlayHitConfig ? (hitSource ?? actor) : actor,
                    context.IncrementalEffects));
            }

            animations.Add(clip, players);
            effectActors.Add(clip, playingActors);
        }

        for (var i = 0; i < players.Count; i++)
        {
            players[i].Sample(clip, LogicTime(clip, time, effectActors[clip][i]));
        }
    }

    private void SampleEffect(TimelineClipPlayEffectConfig clip, float time)
    {
        var entering = !effects.TryGetValue(clip, out var instances);
        if (entering)
        {
            var actors = Resolve(clip, clip.Subject);
            instances = new List<TimelineEffectPlayback>(actors.Count);
            effects.Add(clip, instances);
            effectActors.Add(clip, actors);
            for (var i = 0; i < actors.Count; i++)
            {
                var go = context.SpawnEffect(clip.EffectPath);
                if (go != null)
                {
                    instances.Add(new TimelineEffectPlayback(go, context.EffectLight?.Invoke(clip.EffectPath), context.IncrementalEffects));
                }
            }
        }

        var point = clip.PointData;
        var applyTransform = entering
            || (point.NeedSetParent ? point.UseOriginalRotation || point.UseOriginalScale : !point.DontFollow);
        var casters = Resolve(clip, TimelineUnitSubjectType.Caster);
        var casterOrigin = casters.Count > 0 ? casters[0].Root.position : Vector3.zero;
        for (var i = 0; i < instances.Count && i < effectActors[clip].Count; i++)
        {
            var actor = effectActors[clip][i];
            if (applyTransform)
            {
                effectSubject.Clear();
                effectSubject.Add(actor);
                TimelineActorTransform.Apply(instances[i].Instance.transform, effectSubject, point, casterOrigin);
            }

            instances[i].Sample(LogicTime(clip, time, actor), clip.Duration);
        }
    }

    private void AlignBulletVisuals()
    {
        foreach (var instances in bullets.Values)
        {
            for (var i = 0; i < instances.Count; i++)
            {
                instances[i].AlignVisual(context.Camera);
            }
        }
    }

    private void SampleBullet(TimelineClipPlayBulletConfig clip, float time)
    {
        if (!bullets.TryGetValue(clip, out var instances))
        {
            instances = new List<TimelineBulletPlayback>();
            var targets = Resolve(clip, TimelineUnitSubjectType.EffectTargets);
            var sources = Resolve(clip, TimelineUnitSubjectType.EffectSource);
            if (sources.Count > 0)
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    var go = context.SpawnEffect(clip.Path);
                    if (go != null)
                    {
                        instances.Add(new TimelineBulletPlayback(clip, sources[0], targets[i], go, context.EffectLight?.Invoke(clip.Path), context.IncrementalEffects));
                    }
                }
            }

            bullets.Add(clip, instances);
        }

        for (var i = 0; i < instances.Count; i++)
        {
            instances[i].Sample(time);
        }
    }

    private void SampleShake(TimelineClipCameraShakeConfig clip, float time)
    {
        if (!context.ShakeTransform)
        {
            return;
        }

        var seed = 0;
        foreach (var c in clip.ClipKey)
        {
            seed = unchecked(seed * 31 + c);
        }

        var phase = time / Mathf.Max(clip.Duration, 0.001f) * Mathf.Max(clip.Vibrato, 1) * Mathf.PI * 2f;
        var offset = seed % 997 * 0.01f;
        var randomness = Mathf.Clamp01(clip.Randomness / 180f);
        var noise = new Vector3(
            Mathf.Sin(phase),
            Mathf.Sin(phase + offset * randomness),
            Mathf.Sin(phase + 2f * offset * randomness));
        var envelope = TimelineClipCameraShakeUtil.EvaluateEnvelope(clip.EnvelopeType, time / Mathf.Max(clip.Duration, 0.001f));
        context.ShakeTransform.localPosition = cameraPosition + Vector3.Scale(noise, clip.PositionStrength) * envelope;
        context.ShakeTransform.localRotation = cameraRotation * Quaternion.Euler(Vector3.Scale(noise, clip.RotationStrength) * envelope);
    }

    private float MapAnimatorTime(TimelineClipConfigBase clip, float time, bool toGlobal)
    {
        if (clip is not TimelineClipPlayAnimatorConfig anim || !anim.AffectedByHitStop || casterFreezeWindows is not { Count: > 0 })
        {
            return time;
        }

        if (!toGlobal)
        {
            return TimelineClipHitStopUtil.GetElapsedExcludingFreeze(0f, time, casterFreezeWindows);
        }

        foreach (var window in casterFreezeWindows)
        {
            if (window.Start > time)
            {
                break;
            }

            time += window.Duration;
        }

        return time;
    }

    private float LogicTime(TimelineClipConfigBase clip, float time, BattleUnitTimelineActor actor)
    {
        var affected = clip is TimelineClipPlayEffectConfig effect && effect.AffectedByHitStop
            || clip is TimelineClipPlayHitConfig hit && hit.AffectedByHitStop
            || clip is TimelineClipTranslateConfig move && move.AffectedByHitStop
            || clip is TimelineClipPlayAnimatorConfig anim && anim.AffectedByHitStop;
        if (!affected)
        {
            return time;
        }

        var start = schedule.GetStart(clip);
        return Mathf.Max(0f, TimelineClipHitStopUtil.GetElapsedExcludingFreeze(start, start + time, GetFreezeWindows(clip, actor)));
    }

    private List<TimelineClipHitStopUtil.FreezeWindow> GetFreezeWindows(
        TimelineClipConfigBase clip, BattleUnitTimelineActor actor)
    {
        if (freezes.TryGetValue((clip, actor), out var cached))
        {
            return cached;
        }

        var start = clip is null ? 0f : schedule.GetStart(clip);
        var end = clip is null ? float.PositiveInfinity : start + clip.Duration;
        windows.Clear();
        foreach (var item in config.Clips)
        {
            if (item is not TimelineClipHitStopConfig stop)
            {
                continue;
            }

            var targets = Resolve(stop, stop.Subject);
            if (actor is not null && !targets.Contains(actor))
            {
                continue;
            }

            var from = Mathf.Max(start, schedule.GetStart(stop));
            var to = Mathf.Min(end, schedule.GetEnd(stop));
            if (to > from)
            {
                windows.Add(new TimelineClipHitStopUtil.FreezeWindow { Start = from, End = to });
            }
        }

        windows.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (var i = windows.Count - 1; i > 0; i--)
        {
            if (windows[i].Start <= windows[i - 1].End)
            {
                windows[i - 1] = new TimelineClipHitStopUtil.FreezeWindow
                {
                    Start = windows[i - 1].Start,
                    End = Mathf.Max(windows[i - 1].End, windows[i].End),
                };
                windows.RemoveAt(i);
            }
        }

        cached = new List<TimelineClipHitStopUtil.FreezeWindow>(windows);
        freezes.Add((clip, actor), cached);
        return cached;
    }

    private void Exit(TimelineClipConfigBase clip)
    {
        environment.Exit(clip);
        if (animations.Remove(clip, out var players))
        {
            for (var i = 0; i < players.Count; i++)
            {
                players[i].Exit(clip);
            }
        }

        if (effects.Remove(clip, out var effectInstances))
        {
            for (var i = 0; i < effectInstances.Count; i++)
            {
                effectInstances[i].Release();
                context.DespawnEffect(effectInstances[i].Instance);
            }
        }

        if (bullets.Remove(clip, out var instances))
        {
            for (var i = 0; i < instances.Count; i++)
            {
                instances[i].Release();
                context.DespawnEffect(instances[i].Instance);
            }
        }

        effectActors.Remove(clip);
        if (clip is TimelineClipVisibilityConfig visibilityClip)
        {
            visibility.Exit(visibilityClip);
        }

        if (!Supports(clip))
        {
            context.ExitEditorClip?.Invoke(clip);
        }

        if (clip is TimelineClipCameraShakeConfig)
        {
            ResetCamera();
        }
    }

    private void Reset()
    {
        environment.Reset();
        visibility.Reset();
        units.Reset();
        camera.Reset();
        resolved.Clear();
        freezes.Clear();
        unitClocks.Clear();
        context.ResetEditorPlayback?.Invoke();
        ResetCamera();
    }

    private void ResetCamera()
    {
        if (context.ShakeTransform)
        {
            context.ShakeTransform.localPosition = cameraPosition;
            context.ShakeTransform.localRotation = cameraRotation;
        }
    }
}
