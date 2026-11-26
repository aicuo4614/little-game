using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TimelineClipPlayBulletConfig : TimelineClipConfigBase
{
    public string Path;
    public float MoveSpeed;

    /// <summary>视觉朝向模式；默认保持旧的三维行为。CameraBillboard 需要独立的 Spine 子节点。</summary>
    public TimelineBulletFacingType FacingType;
    /// <summary>Billboard 素材的平面角度修正（度）；朝右为 0，朝上为 -90。</summary>
    public float BillboardAngleOffset;

    /// <summary>
    /// 子弹出现后先停在起点、再开始沿弹道移动的时间（秒）。旧 JSON 缺字段为 0。
    /// Clip 总时长 = 延迟移动 + 飞行时长。
    /// </summary>
    public float MoveDelayTime;

    /// <summary>
    /// 延迟段缩放关键帧列表。仅当起点勾选「重置缩放」且 <see cref="MoveDelayTime"/> &gt; 0 时生效。
    /// 每项：Scale(XYZ) + Duration（本段持续秒）。从第 0 项 Scale 起，按 Duration 依次插值到下一项，
    /// 最后一项 Duration 结束后接到起点 <see cref="TimelineClipPointData.OffsetScale"/>。
    /// 例：[(0,0,0,0.5),(1.3,1.3,1.3,0.5)] + 缩放(1,1,1)、延迟 1 → 0s=0，0.5s=1.3，1s=1。
    /// 旧 JSON 的 <c>InitialScale</c> 已废弃；缺列表视为无渐变（全程用起点缩放）。
    /// </summary>
    public List<TimelineBulletDelayScaleKey> DelayScaleKeys = new List<TimelineBulletDelayScaleKey>();

    /// <summary>弹道类型。旧 JSON 缺字段为抛物线。</summary>
    public TimelineBulletTrajectoryType TrajectoryType;

    /// <summary>
    /// 抛物线弧高系数：控制点相对弦中点上抬「起点到终点距离 * 该值」。默认 0.35 对齐旧硬编码。
    /// </summary>
    public float ArcHeightFactor = 0.35f;

    public TimelineClipPointData StartData;
    public TimelineClipPointData EndData;
}
