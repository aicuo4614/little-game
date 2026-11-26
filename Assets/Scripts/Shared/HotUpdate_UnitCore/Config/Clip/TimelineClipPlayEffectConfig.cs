using System;
using UnityEngine;

[Serializable]
public class TimelineClipPlayEffectConfig : TimelineClipConfigBase
{
    public string EffectPath;
    public TimelineUnitSubjectType Subject;
    public TimelineClipPointData PointData;

    /// <summary>是否受角色定帧（HitStop）影响；默认 true。关闭后特效跟全局轴，不定帧期间不冻粒子/动画。</summary>
    public bool AffectedByHitStop = true;
}
