using System;
using DG.Tweening;
using UnityEngine;

/// <summary>固定战斗偏航的运镜。所有路径沿相机的水平右向、景深和世界高度求值。</summary>
internal static class TimelineCameraPlanarShotUtil
{
    internal static void Validate(TimelineClipCameraShotConfig shot)
    {
        if (shot.Use2DComposition && (float.IsNaN(shot.PlanarTravel) || float.IsInfinity(shot.PlanarTravel)))
        {
            throw new ArgumentException("2D camera travel must be finite: " + shot.ClipKey);
        }
    }

    /// <summary>以注视点求机位平移与俯仰，始终输出同一个世界偏航和零滚转。</summary>
    internal static void Evaluate(TimelineClipCameraShotConfig shot, TimelineCameraShotState state, float time, float duration,
        out Vector3 position, out Quaternion rotation, out float fov)
    {
        var progress = duration <= 0.0001f ? 1f : Mathf.Clamp01(time / duration);
        var eased = DOVirtual.EasedValue(0f, 1f, progress, shot.PathEase);
        var right = state.BattleYaw * Vector3.right;
        var forward = state.BattleYaw * Vector3.forward;
        var look = state.LookPoint;
        var fromCurrent = shot.ShotType == TimelineCameraShotType.Hold ? shot.HoldKeepEntryPose : shot.FromCurrentPose;

        if (shot.ShotType == TimelineCameraShotType.Hold)
        {
            position = fromCurrent ? state.EntryPosition
                : look + TimelineTransformUtil.FromAuthoringPos(shot.HoldOffset, state.Mirror);
        }
        else if (shot.ShotType == TimelineCameraShotType.Ots)
        {
            var shoulderOffset = right * Mirror(shot.MoveAmount, state.Mirror) + Vector3.up * shot.Height
                - forward * Mathf.Max(0.1f, shot.Distance);
            var shoulderPose = state.FromPoint + shoulderOffset;
            position = fromCurrent ? Vector3.LerpUnclamped(state.EntryPosition, shoulderPose, eased) : shoulderPose;
        }
        else
        {
            var firstLook = state.FirstLook;
            if (shot.ShotType == TimelineCameraShotType.PivotBlend
                || shot.ShotType == TimelineCameraShotType.WhipPan && !fromCurrent)
            {
                firstLook = state.FirstFrom;
                look = Vector3.LerpUnclamped(state.FromPoint, look, eased);
            }

            var offset = EvaluateOffset(shot, state, firstLook, right, forward, fromCurrent, eased);
            position = look + right * offset.x + Vector3.up * offset.y - forward * offset.z;
        }

        var depth = Mathf.Max(0.01f, Vector3.Dot(look - position, forward));
        var pitch = Mathf.Atan2(position.y - look.y, depth) * Mathf.Rad2Deg;
        if (fromCurrent)
        {
            // 接镜保留进入俯仰；偏航由运镜过渡提前接回战斗基准。
            pitch = Mathf.LerpAngle(state.EntryRotation.eulerAngles.x, pitch, eased);
        }

        rotation = state.BattleYaw * Quaternion.AngleAxis(pitch, Vector3.right);
        fov = shot.ShotType == TimelineCameraShotType.DollyZoom
            ? TimelineClipCameraShotUtil.EvaluateFovForced(shot, eased, fromCurrent, state.EntryFov)
            : TimelineClipCameraShotUtil.EvaluateFov(shot, eased, fromCurrent, state.EntryFov);
    }

    // x 是水平右向，y 是高度，z 是相机与构图点之间的水平景深。
    private static Vector3 EvaluateOffset(TimelineClipCameraShotConfig shot, TimelineCameraShotState state,
        Vector3 firstLook, Vector3 right, Vector3 forward, bool fromCurrent, float eased)
    {
        var depthChanges = shot.ShotType is TimelineCameraShotType.PushIn or TimelineCameraShotType.PullOut
            or TimelineCameraShotType.DollyZoom or TimelineCameraShotType.Orbit
            or TimelineCameraShotType.Helix or TimelineCameraShotType.DollyBoom;
        var entry = state.EntryPosition - firstLook;
        var start = fromCurrent ? new Vector3(Vector3.Dot(entry, right), entry.y, -Vector3.Dot(entry, forward))
            : new Vector3(0f, shot.Height, Mathf.Max(0.1f, depthChanges ? shot.StartDistance : shot.Distance));
        var end = start;

        switch (shot.ShotType)
        {
            case TimelineCameraShotType.PushIn:
            case TimelineCameraShotType.PullOut:
            case TimelineCameraShotType.DollyZoom:
            {
                end.x = 0f;
                end.z = shot.ShotType == TimelineCameraShotType.PullOut
                    ? Mathf.Max(0.1f, start.z) + Mathf.Abs(shot.MoveAmount) : Mathf.Max(0.1f, shot.Distance);
                if (fromCurrent)
                {
                    end.y = start.z > 0.1f ? start.y * (end.z / start.z) : shot.Height;
                }
                break;
            }
            case TimelineCameraShotType.BoomUp:
            case TimelineCameraShotType.BoomDown:
            {
                end.y += Mathf.Abs(shot.MoveAmount) * (shot.ShotType == TimelineCameraShotType.BoomDown ? -1f : 1f);
                break;
            }
            case TimelineCameraShotType.Truck:
            case TimelineCameraShotType.Crane:
            {
                end.x += Mirror(shot.MoveAmount, state.Mirror);
                end.y += shot.ShotType == TimelineCameraShotType.Crane ? shot.BoomAmount : 0f;
                break;
            }
            case TimelineCameraShotType.Helix:
            case TimelineCameraShotType.DollyBoom:
            {
                end.y += shot.MoveAmount;
                goto case TimelineCameraShotType.Orbit;
            }
            case TimelineCameraShotType.Orbit:
            {
                end.x += Mirror(shot.PlanarTravel, state.Mirror);
                end.z = Mathf.Max(0.1f, shot.Distance);
                break;
            }
            case TimelineCameraShotType.PivotBlend:
            case TimelineCameraShotType.WhipPan:
            {
                end.x = 0f;
                end.z = Mathf.Max(0.1f, start.z);
                break;
            }
        }

        return Vector3.LerpUnclamped(start, end, eased);
    }

    private static float Mirror(float amount, bool mirror) => mirror ? -amount : amount;
}
