using System.Collections.Generic;
using UnityEngine;

/// <summary>子弹飞行表现实体；编辑器预览与运行时 Bullet 共用。</summary>
public class BulletEntity
{
    private TimelineClipPlayBulletConfig config;

    private Transform transform;
    private UnitMono unit;
    private UnitMono target;
    private List<UnitMono> targets = new List<UnitMono>();

    private Vector3 initPosition;
    private Quaternion initRotation;
    /// <summary>直线弹道在起飞时锁定的终点；抛物线每帧重算。</summary>
    private Vector3 lockedEndPosition;

    public Vector3 position;
    private float moveDelay;
    private float flyDuration;
    private Vector3 lastPosition;
    private System.Action<UnitMono> finishCallback;
    private bool flightStarted;
    private bool finished;

    /// <summary>飞行段时长（不含延迟移动）。</summary>
    public float FlyDuration => flyDuration;

    /// <summary>总时长 = 延迟移动 + 飞行。</summary>
    public float TotalDuration => moveDelay + flyDuration;

    public void Init(
        UnitMono unit,
        UnitMono target,
        Transform bulletTransform,
        TimelineClipPlayBulletConfig config,
        System.Action<UnitMono> finishCallback = null)
    {
        this.unit = unit;
        this.target = target;
        targets.Clear();
        targets.Add(target);
        transform = bulletTransform;
        this.config = config;
        this.finishCallback = finishCallback;
        flightStarted = false;
        finished = false;

        moveDelay = Mathf.Max(0f, config.MoveDelayTime);
        flyDuration = TimelineClipPlayBulletUtil.ComputeFlyDuration(this.unit, this.config, target);

        ApplyStartPose();
        CaptureFlightStartFromCurrent();
        SyncRootTransformKeepFacing(forceKeepStartFacing: true);
        ApplyDelayScaleIfNeeded(0f);
    }

    public static float ComputeFlyDuration(UnitMono unit, TimelineClipPlayBulletConfig config, UnitMono target)
    {
        return TimelineClipPlayBulletUtil.ComputeFlyDuration(unit, config, target);
    }

    public static float ComputeTotalDuration(UnitMono unit, TimelineClipPlayBulletConfig config, UnitMono target)
    {
        return TimelineClipPlayBulletUtil.ComputeTotalDuration(unit, config, target);
    }

    private float timing;
    private float lastTiming;

    public void Tick(float timing)
    {
        this.timing = timing;
        if (finished)
        {
            return;
        }

        if (timing < moveDelay)
        {
            if (TimelineClipPlayBulletUtil.NeedPerFrameStartApply(config.StartData))
            {
                ApplyStartPose();
                CaptureFlightStartFromCurrent();
            }

            SyncRootTransformKeepFacing(forceKeepStartFacing: true);
            ApplyDelayScaleIfNeeded(timing);
            return;
        }

        if (!flightStarted)
        {
            BeginFlight();
        }

        var flyTiming = timing - moveDelay;
        var trackingEnd = ResolveTrackingEnd();
        var t = Mathf.Clamp01(flyTiming / Mathf.Max(flyDuration, 0.0001f));
        position = TimelineClipPlayBulletUtil.EvaluatePosition(
            config,
            initPosition,
            trackingEnd,
            flyTiming,
            flyDuration);
        SyncRootTransformKeepFacing(forceKeepStartFacing: false);

        if (t >= 1f)
        {
            position = trackingEnd;
            SyncRootTransformKeepFacing(forceKeepStartFacing: false);
            finished = true;
            finishCallback?.Invoke(target);
        }
    }

    private void BeginFlight()
    {
        flightStarted = true;
        if (TimelineClipPlayBulletUtil.NeedPerFrameStartApply(config.StartData))
        {
            ApplyStartPose();
        }

        if (transform.parent != null)
        {
            transform.SetParent(null, true);
        }

        CaptureFlightStartFromCurrent();
        TimelineClipPlayBulletUtil.GetStartEndWorldPos(
            unit,
            config,
            target,
            out _,
            out lockedEndPosition);
        lastPosition = initPosition;
        position = initPosition;
        // 起飞时缩放到目标「缩放」（延迟渐变终点）
        if (TimelineClipPlayBulletUtil.NeedsDelayScaleLerp(config))
        {
            transform.localScale = config.StartData.OffsetScale;
        }
    }

    /// <summary>
    /// 延迟段：勾选重置缩放时，按 DelayScaleKeys 分段渐变到 OffsetScale。
    /// 须在 ApplyStartPose 之后调用（后者会先写成 OffsetScale）。
    /// </summary>
    private void ApplyDelayScaleIfNeeded(float timing)
    {
        if (!TimelineClipPlayBulletUtil.NeedsDelayScaleLerp(config))
        {
            return;
        }

        transform.localScale = TimelineClipPlayBulletUtil.EvaluateDelayScale(config, timing);
    }

    /// <summary>按 StartData 写起点位姿（重置旋转/缩放/叠加朝向与 PlayEffect 相同）。</summary>
    private void ApplyStartPose()
    {
        var startData = config.StartData;
        TimelineTransformUtil.GetTransformationFromUnitNodePointData(
            unit.transform.position,
            unit,
            startData,
            out var startPosition,
            out var startRotation,
            out var startScale);

        var parent = TimelineTransformUtil.GetParentFromUnitNodePointData(startData, unit);
        TimelineTransformUtil.ApplyEffectTransform(
            transform,
            startData,
            parent,
            startPosition,
            startRotation,
            startScale,
            unit);

        position = transform.position;
        initRotation = transform.rotation;
    }

    private void CaptureFlightStartFromCurrent()
    {
        initPosition = transform.position;
        position = initPosition;
        lastPosition = initPosition;
    }

    /// <summary>直线用起飞时锁定终点；抛物线每帧按 EndData 重算（含偏移）。</summary>
    private Vector3 ResolveTrackingEnd()
    {
        if (config.TrajectoryType == TimelineBulletTrajectoryType.Linear)
        {
            return lockedEndPosition;
        }

        TimelineClipPlayBulletUtil.GetStartEndWorldPos(unit, config, target, out _, out var end);
        return end;
    }

    private void SyncRootTransformKeepFacing(bool forceKeepStartFacing)
    {
        var delta = position - lastPosition;
        transform.position = position;

        if (forceKeepStartFacing)
        {
            transform.rotation = initRotation;
            lastPosition = position;
            return;
        }

        // 飞行段：编辑器与运行时统一按位移朝向
        if (Application.isPlaying)
        {
            if (delta.sqrMagnitude > 0.0001f)
            {
                transform.rotation = CheckRotation(delta);
            }
        }
        else
        {
            if (!System.Math.Round(timing, 2).Equals(System.Math.Round(lastTiming, 2)))
            {
                lastTiming = timing;
                if (delta.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = CheckRotation(delta);
                }
            }
        }

        lastPosition = position;
    }

    private Quaternion CheckRotation(Vector3 delta)
    {
        if (transform.localScale.x < 0)
        {
            delta.x = -delta.x;
        }

        if (transform.localScale.y < 0)
        {
            delta.y = -delta.y;
        }

        if (transform.localScale.z < 0)
        {
            delta.z = -delta.z;
        }

        return Quaternion.LookRotation(delta, Vector3.up);
    }
}
