using System;
using UnityEngine;

[Serializable]
public class TimelineClipPlayHitConfig : TimelineClipConfigBase
{
    /// <summary>目标的 Spine 受击动画名。留空时仍按片段起点发出命中通知。</summary>
    public string SpineAnimationName;

    /// <summary>受击动画相对于命中点的延迟（秒）。</summary>
    public float SpineAnimationDelay;

    public string AnimatorParamName;
    public float AnimatorDelayTime;
    public string EffectPath;
    public TimelineClipPointData UnitNodePointData;

    /// <summary>是否受角色定帧（HitStop）影响；默认 true。同时作用于受击动画与受击特效（粒子采样/定帧暂停）。</summary>
    public bool AffectedByHitStop = true;

    public TimelineClipPlayHitConfig()
    {
        Type = TimelineClipType.PlayHit;
    }
}
