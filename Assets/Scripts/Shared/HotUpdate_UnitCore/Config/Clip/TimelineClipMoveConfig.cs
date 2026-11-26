using DG.Tweening;
using System;
using UnityEngine;

[Serializable]
public class TimelineClipMoveConfig : TimelineClipConfigBase
{
    public TimelineMoveEntityType MoveEntityType;
    public string EffectPath;
    public Ease MoveToEase = Ease.Linear;
    public Vector3 OffestPos;
    public Vector3 OffsetEuler;

    public TranslateMovePointType TranslateMovePointType;
    public RotateMovePointType RotateMovePointType;

    /// <summary>
    /// 仅相机：勾选后按 TeamA 侧编辑的构图，在 TeamB 施法时对落点/朝向做战场中线（X）镜像，避免对侧绕半圈。
    /// </summary>
    public bool MirrorByTeam;

    /// <summary>
    /// 仅相机：锁定水平（禁止荷兰角）。默认勾选。勾选后 Scene 旋转手柄与最终朝向都以世界 up 为上，
    /// <see cref="OffsetEuler"/>.z（roll）强制为 0，避免世界轴/自由球把手把相机拧歪。
    /// </summary>
    public bool LockCameraUpright = true;

    /// <summary>是否受角色定帧（HitStop）影响；默认 true。关闭后移动跟全局轴，条长不加定帧重叠。</summary>
    public bool AffectedByHitStop = true;

    /// <summary>勾选后可用追近速度加快「朝向红点」；速度 0 与不勾选相同。</summary>
    public bool FastApproachFinalEuler;

    /// <summary>
    /// 旋转追近速度（0~1）。仅勾选时生效：0=整段按缓动朝向红点（同不勾选），1=全程盯红点。
    /// </summary>
    public float FastApproachFinalEulerSpeed;
}
