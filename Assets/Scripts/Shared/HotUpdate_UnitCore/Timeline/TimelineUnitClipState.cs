using UnityEngine;

/// <summary>单位片段进入时的快照，仅保存该次采样所需的数据。</summary>
public sealed class TimelineUnitClipState
{
    /// <summary>作用单位。</summary>
    public BattleUnitTimelineActor Actor;
    /// <summary>进入片段的位置。</summary>
    public Vector3 Position;
    /// <summary>进入片段的表现旋转。</summary>
    public Quaternion Rotation;
    /// <summary>进入片段的表现缩放。</summary>
    public Vector3 Scale;
    /// <summary>进入时确定的终点或朝向参考位置。</summary>
    public Vector3 End;
    /// <summary>进入时逻辑根朝向。</summary>
    public Quaternion RootRotation;
    /// <summary>进入时 Spine 左右朝向。</summary>
    public float Facing;
    /// <summary>进入时捕获的不带偏移的参考点。</summary>
    public Vector3 Anchor;
    /// <summary>参考挂点旋转，仅用于挂点局部偏移。</summary>
    public Quaternion AnchorRotation;
    /// <summary>参考挂点世界缩放，仅用于挂点局部偏移。</summary>
    public Vector3 AnchorScale;
    /// <summary>起点与参考点重合时捕获的备用方向。</summary>
    public Vector3 OffsetDirection;
}
