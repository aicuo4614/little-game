using UnityEngine;

/// <summary>单条运镜的进入快照与注视状态；不从已运动的相机或 billboard forward 反推构图。</summary>
public sealed class TimelineCameraShotState
{
    /// <summary>进入片段时的世界机位。</summary>
    public Vector3 EntryPosition;
    /// <summary>进入片段时的世界朝向。</summary>
    public Quaternion EntryRotation;
    /// <summary>恢复演出时的战斗机位偏航，供 2D 构图使用。</summary>
    public Quaternion BattleYaw;
    /// <summary>进入片段时的 FOV。</summary>
    public float EntryFov;
    /// <summary>战场 X 镜像状态。</summary>
    public bool Mirror;
    /// <summary>片段进入时确定的水平构图方向。</summary>
    public Vector3 Facing;
    /// <summary>注视终点对象快照。</summary>
    public TimelineCameraPivotPlayback Look;
    /// <summary>注视起点或肩位对象快照；普通运镜不使用。</summary>
    public TimelineCameraPivotPlayback From;
    /// <summary>定格锁定的第一帧终点。</summary>
    public Vector3 FirstLook;
    /// <summary>进入片段时的起始枢轴，用于保存 2D 换枢轴的入口构图。</summary>
    public Vector3 FirstFrom;
    /// <summary>缓存前后端当前实际世界点，供场景手柄读取。</summary>
    public Vector3 LookPoint;
    /// <summary>起始枢轴或肩位世界点。</summary>
    public Vector3 FromPoint;
    /// <summary>缓存键，与其他播放器实例隔离。</summary>
    public string Owner;

    /// <summary>按共享路径算法求值，单位节点仍可跟随运动。</summary>
    public void Evaluate(TimelineClipCameraShotConfig shot, float time, float duration,
        out Vector3 position, out Quaternion rotation, out float fov)
    {
        LookPoint = shot.ShotType == TimelineCameraShotType.Hold && shot.HoldLockFirstFramePose ? FirstLook : Look.Point;
        FromPoint = From is not null ? From.Point : LookPoint;
        if (shot.Use2DComposition)
        {
            TimelineCameraPlanarShotUtil.Evaluate(shot, this, time, duration, out position, out rotation, out fov);
            return;
        }

        TimelineClipCameraShotUtil.EvaluateResolved(shot, time, duration, LookPoint, FromPoint, FromPoint,
            Facing, Mirror, true, EntryPosition, EntryRotation, EntryFov, Owner,
            out position, out rotation, out fov, useCapturedShoulderAxis: true);
    }
}
