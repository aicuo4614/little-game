using System;
using UnityEngine;

[Serializable]
public class TimelineClipScaleConfig : TimelineClipUnitConfigBase
{
    public DurationType DurationType;
    public float DurationPraram;
    /// <summary>相对进入片段时表现节点缩放的起点倍率。</summary>
    public Vector3 StartScale = Vector3.one;
    /// <summary>相对进入片段时表现节点缩放的终点倍率。</summary>
    public Vector3 EndScale = Vector3.one;
}
