using System;
using UnityEngine;

[Serializable]
public class TimelineClipForceConfig : TimelineClipConfigBase
{
    public HitTranslateType HitTranslateType;
    public Vector3 Force;

    /// <summary>是否受角色定帧（HitStop）影响；默认 true。关闭后受力位移在定帧期间仍推进。</summary>
    public bool AffectedByHitStop = true;
}
