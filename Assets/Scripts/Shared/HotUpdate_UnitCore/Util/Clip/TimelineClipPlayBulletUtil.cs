using UnityEngine;

/// <summary>PlayBullet 共用：飞行时长与弹道采样（编辑器预览与运行时 BulletEntity）。</summary>
public static class TimelineClipPlayBulletUtil
{
    public const float MinFlyDuration = 0.15f;
    public const float DefaultArcHeightFactor = 0.35f;

    /// <summary>按起点/终点直线距离与飞行速度估算飞行段时长，下限 <see cref="MinFlyDuration"/>。</summary>
    public static float ComputeFlyDuration(UnitMono unit, TimelineClipPlayBulletConfig config, UnitMono target)
    {
        GetStartEndWorldPos(unit, config, target, out var startPosition, out var endPosition);
        var speed = Mathf.Max(config.MoveSpeed, 0.01f);
        return Mathf.Max(MinFlyDuration, Vector3.Distance(startPosition, endPosition) / speed);
    }

    /// <summary>Clip 总时长 = 延迟移动 + 飞行时长。</summary>
    public static float ComputeTotalDuration(UnitMono unit, TimelineClipPlayBulletConfig config, UnitMono target)
    {
        return Mathf.Max(0f, config.MoveDelayTime) + ComputeFlyDuration(unit, config, target);
    }

    public static void GetStartEndWorldPos(
        UnitMono unit,
        TimelineClipPlayBulletConfig config,
        UnitMono target,
        out Vector3 startPosition,
        out Vector3 endPosition)
    {
        var startData = config.StartData;
        var endData = config.EndData;

        TimelineTransformUtil.GetTransformationFromUnitNodePointData(
            unit.transform.position,
            unit,
            startData,
            out startPosition,
            out _,
            out _);
        TimelineTransformUtil.GetTransformationFromUnitNodePointData(
            unit.transform.position,
            target,
            endData,
            out endPosition,
            out _,
            out _);
    }

    /// <summary>
    /// 起点挂载/跟随语义与 PlayEffect 相同：挂载时仅重置旋转/缩放需每帧回写；
    /// 未挂载且未勾「不跟随」则每帧跟起点世界位姿（延迟移动阶段用）。
    /// </summary>
    public static bool NeedPerFrameStartApply(TimelineClipPointData startData)
    {
        if (startData == null)
        {
            return false;
        }

        if (startData.NeedSetParent)
        {
            return startData.UseOriginalRotation || startData.UseOriginalScale;
        }

        return !startData.DontFollow;
    }

    /// <summary>
    /// 是否在延迟移动段做缩放渐变：勾选起点「重置缩放」且延迟时间 &gt; 0。
    /// </summary>
    public static bool NeedsDelayScaleLerp(TimelineClipPlayBulletConfig config)
    {
        if (config == null || config.StartData == null)
        {
            return false;
        }

        return config.StartData.UseOriginalScale && config.MoveDelayTime > 0f;
    }

    /// <summary>
    /// 延迟段缩放：按 <see cref="TimelineClipPlayBulletConfig.DelayScaleKeys"/> 分段插值，
    /// 最后接到 <see cref="TimelineClipPointData.OffsetScale"/>。
    /// 列表为空则全程为终点缩放。
    /// </summary>
    public static Vector3 EvaluateDelayScale(TimelineClipPlayBulletConfig config, float timing)
    {
        var endScale = config.StartData.OffsetScale;
        var keys = config.DelayScaleKeys;
        if (keys == null || keys.Count == 0)
        {
            return endScale;
        }

        var delay = Mathf.Max(config.MoveDelayTime, 0f);
        var t = Mathf.Clamp(timing, 0f, delay);
        var elapsed = 0f;
        for (var i = 0; i < keys.Count; i++)
        {
            var from = keys[i].Scale;
            var to = i + 1 < keys.Count ? keys[i + 1].Scale : endScale;
            var segDur = Mathf.Max(keys[i].Duration, 0f);
            if (segDur <= 0.0001f)
            {
                if (t <= elapsed)
                {
                    return from;
                }

                continue;
            }

            if (t <= elapsed + segDur)
            {
                var u = (t - elapsed) / segDur;
                return Vector3.Lerp(from, to, u);
            }

            elapsed += segDur;
        }

        return endScale;
    }

    /// <summary>
    /// 按弹道类型采样世界坐标。抛物线每帧用当前终点；直线由调用方传入 Init 锁定的终点。
    /// <paramref name="flyTiming"/> 为扣除延迟移动后的飞行时间。
    /// </summary>
    public static Vector3 EvaluatePosition(
        TimelineClipPlayBulletConfig config,
        Vector3 start,
        Vector3 trackingEnd,
        float flyTiming,
        float flyDuration)
    {
        var duration = Mathf.Max(flyDuration, 0.0001f);
        var t = Mathf.Clamp01(flyTiming / duration);
        if (config.TrajectoryType == TimelineBulletTrajectoryType.Linear)
        {
            return Vector3.Lerp(start, trackingEnd, t);
        }

        return EvaluateParabola(config, start, trackingEnd, t);
    }

    private static Vector3 EvaluateParabola(
        TimelineClipPlayBulletConfig config,
        Vector3 start,
        Vector3 end,
        float t)
    {
        var control = ComputeParabolaControlPoint(start, end, config.ArcHeightFactor);
        return EvaluateQuadraticBezier(start, control, end, t);
    }

    /// <summary>二次贝塞尔控制点：弦中点 + 世界上方向 * 距离 * 弧高系数。</summary>
    public static Vector3 ComputeParabolaControlPoint(Vector3 start, Vector3 end, float arcHeightFactor)
    {
        var mid = (start + end) * 0.5f;
        var arcHeight = Vector3.Distance(start, end) * arcHeightFactor;
        return mid + Vector3.up * arcHeight;
    }

    public static Vector3 EvaluateQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        var u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }
}
