using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>运镜与过渡的共享执行器。宿主只提供相机和对象绑定；倒拖由共同调度器重放进入快照。</summary>
public sealed class TimelineCameraPlayback
{
    private TimelineConfig config;
    private TimelineSchedule schedule;
    private TimelinePlaybackContext context;
    private Dictionary<TimelineClipCameraShotConfig, TimelineCameraShotState> states = new Dictionary<TimelineClipCameraShotConfig, TimelineCameraShotState>();
    private Dictionary<TimelineClipCameraShotBlendConfig, TimelineCameraShotState> blends = new Dictionary<TimelineClipCameraShotBlendConfig, TimelineCameraShotState>();
    private Dictionary<TimelineClipCameraShotBlendConfig, TimelineCameraShotState> destinations = new Dictionary<TimelineClipCameraShotBlendConfig, TimelineCameraShotState>();
    private string owner = Guid.NewGuid().ToString();
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Quaternion battleYaw;
    private float initialFov;
    private Vector3 position;
    private Quaternion rotation;
    private float fov;
    private bool hasSampled;

    /// <summary>捕获演出进入前的场景机位，停止后准确恢复。</summary>
    public TimelineCameraPlayback(TimelineConfig config, TimelineSchedule schedule, TimelinePlaybackContext context)
    {
        this.config = config;
        this.schedule = schedule;
        this.context = context;
        if (context.CameraRoot && context.Camera)
        {
            initialPosition = context.CameraRoot.position;
            initialRotation = context.CameraRoot.rotation;
            initialFov = context.Camera.fieldOfView;
        }
        else
        {
            initialRotation = Quaternion.identity;
        }
        battleYaw = Quaternion.Euler(0f, initialRotation.eulerAngles.y, 0f);
        position = initialPosition;
        rotation = initialRotation;
        fov = initialFov;
    }

    /// <summary>读取与画面相同的枢轴；尚未进入的运镜由编辑器自行解析静态参考。</summary>
    public bool TryGetPivot(string key, bool from, out Vector3 point)
    {
        foreach (var pair in states)
        {
            if (pair.Key.ClipKey == key)
            {
                point = from ? pair.Value.FromPoint : pair.Value.LookPoint;
                return true;
            }
        }
        point = default;
        return false;
    }

    /// <summary>采样运镜或过渡。无相机时明确报错，禁止实战静默跳过。</summary>
    public void Sample(TimelineClipConfigBase clip, float time)
    {
        if (!context.CameraRoot || !context.Camera)
        {
            throw new ArgumentException("CameraShot requires CameraRoot and Camera in the playback context.");
        }
        if (clip is TimelineClipCameraShotConfig shot)
        {
            if (!states.TryGetValue(shot, out var state))
            {
                state = Capture(shot, owner);
                states.Add(shot, state);
            }
            state.Evaluate(shot, time, schedule.GetEnd(shot) - schedule.GetStart(shot),
                out position, out rotation, out var sampledFov);
            if (sampledFov > 0f)
            {
                fov = sampledFov;
            }
        }
        else if (clip is TimelineClipCameraShotBlendConfig blend)
        {
            if (!blends.TryGetValue(blend, out var entry))
            {
                entry = new TimelineCameraShotState { EntryPosition = position, EntryRotation = rotation, EntryFov = fov };
                blends.Add(blend, entry);
                var next = Next(blend);
                destinations.Add(blend, Capture(next, owner + blend.ClipKey));
            }
            var target = Next(blend);
            destinations[blend].Evaluate(target, 0f, schedule.GetEnd(target) - schedule.GetStart(target),
                out var endPosition, out var endRotation, out var endFov);
            TimelineClipCameraShotBlendUtil.EvaluateBlend(time, schedule.GetEnd(blend) - schedule.GetStart(blend),
                blend.BlendEase, blend.BlendFov, entry.EntryPosition, entry.EntryRotation, entry.EntryFov,
                endPosition, endRotation, endFov > 0f ? endFov : entry.EntryFov,
                out position, out rotation, out var sampledFov);
            if (sampledFov > 0f)
            {
                fov = sampledFov;
            }
        }
        hasSampled = true;
        context.CameraRoot.SetPositionAndRotation(position, rotation);
        context.Camera.fieldOfView = fov;
    }

    /// <summary>校验显式注视绑定的类型和生效时间，并确认所有过渡有后继运镜。</summary>
    public static void Validate(TimelineConfig config, TimelineSchedule schedule)
    {
        foreach (var clip in config.Clips)
        {
            if (clip is TimelineClipCameraShotConfig shot)
            {
                TimelineCameraPlanarShotUtil.Validate(shot);
                CheckPivot(shot, shot.LookPivot);
                if (TimelineCameraPivotPlayback.UsesFrom(shot))
                {
                    CheckPivot(shot, shot.FromPivot);
                }
            }
            else if (clip is TimelineClipCameraShotBlendConfig blend)
            {
                _ = Next(config, schedule, blend);
            }
        }

        void CheckPivot(TimelineClipCameraShotConfig shot, TimelineCameraPivotData pivot)
        {
            if (pivot is null)
            {
                return;
            }
            TimelineCameraPivotPlayback.Validate(pivot);
            if (pivot.Subject == TimelineUnitSubjectType.Caster || string.IsNullOrEmpty(pivot.BindingKey))
            {
                return;
            }
            var binding = config.Clips.Find(item => item.ClipKey == pivot.BindingKey);
            var expected = pivot.Subject == TimelineUnitSubjectType.CastTargets
                ? TimelineClipType.SetCastTargets : TimelineClipType.SetHitTargets;
            if (binding is null || binding.Type != expected || schedule.GetStart(binding) > schedule.GetStart(shot) + 0.0001f)
            {
                throw new ArgumentException($"Invalid camera pivot binding: clip={shot.ClipKey}, binding={pivot.BindingKey}, expected={expected}");
            }
        }
    }

    private TimelineClipCameraShotConfig Next(TimelineClipCameraShotBlendConfig blend) => Next(config, schedule, blend);

    private static TimelineClipCameraShotConfig Next(TimelineConfig config, TimelineSchedule schedule, TimelineClipCameraShotBlendConfig blend)
    {
        return TimelineClipCameraShotBlendUtil.ResolveNextCameraShot(config.Clips, blend.NextCameraShotClipKey,
            blend.ClipKey, schedule.GetStart(blend), schedule.GetEnd(blend), schedule.GetStart)
            ?? throw new ArgumentException("Camera blend has no following shot: " + blend.ClipKey);
    }

    private TimelineCameraShotState Capture(TimelineClipCameraShotConfig shot, string cacheOwner)
    {
        var casters = context.Resolve(shot, TimelineUnitSubjectType.Caster, null);
        if (casters.Count == 0)
        {
            throw new ArgumentException("Camera shot has no caster: " + shot.ClipKey);
        }
        var caster = casters[0];
        var mirror = TimelineTransformUtil.NeedsTeamCameraMirror(shot.MirrorByTeam, caster.Team);
        var look = new TimelineCameraPivotPlayback(TimelineCameraPivotPlayback.Data(shot, false), shot, context.Resolve, caster, mirror);
        var from = TimelineCameraPivotPlayback.UsesFrom(shot)
            ? new TimelineCameraPivotPlayback(TimelineCameraPivotPlayback.Data(shot, true), shot, context.Resolve, caster, mirror) : null;
        var facing = look.ObjectCenter - (shot.ShotType == TimelineCameraShotType.Ots ? from.ObjectCenter : caster.Root.position);
        facing.y = 0f;
        // 自身构图使用固定战场轴；节点的高度或 billboard 朝向不能成为机位方向。
        var data = TimelineCameraPivotPlayback.Data(shot, false);
        if (shot.ShotType != TimelineCameraShotType.Ots && data.Subject == TimelineUnitSubjectType.Caster
            || facing.sqrMagnitude < 0.000001f)
        {
            facing = caster.Team == UnitTeamType.TeamB ? Vector3.left : Vector3.right;
        }
        return new TimelineCameraShotState
        {
            EntryPosition = position, EntryRotation = rotation, EntryFov = fov,
            Mirror = mirror, Facing = facing.normalized, Look = look, From = from,
            FirstLook = look.Point, FirstFrom = from?.Point ?? look.Point, BattleYaw = battleYaw, Owner = cacheOwner
        };
    }

    /// <summary>停止或倒拖前清理每条运镜的第一帧缓存，并恢复相机。</summary>
    public void Reset()
    {
        foreach (var pair in states)
        {
            TimelineClipCameraShotUtil.ClearHoldFirstFramePose(pair.Value.Owner, pair.Key.ClipKey);
        }
        foreach (var pair in destinations)
        {
            TimelineClipCameraShotUtil.ClearHoldFirstFramePose(pair.Value.Owner, Next(pair.Key).ClipKey);
        }
        states.Clear();
        blends.Clear();
        destinations.Clear();
        position = initialPosition;
        rotation = initialRotation;
        fov = initialFov;
        if (hasSampled && context.CameraRoot && context.Camera)
        {
            context.CameraRoot.SetPositionAndRotation(position, rotation);
            context.Camera.fieldOfView = fov;
        }
        hasSampled = false;
    }
}
