using System;
using UnityEngine;

[Serializable]
public class TimelineClipRotateConfig : TimelineClipUnitConfigBase
{
    public DurationType DurationType;
    public float DurationPraram;

    /// <summary>相对进入片段时表现节点的起点欧拉角，单位为度。</summary>
    public Vector3 StartEuler;
    /// <summary>相对进入片段时表现节点的终点欧拉角，允许超过 360 度。</summary>
    public Vector3 EndEuler;
}

