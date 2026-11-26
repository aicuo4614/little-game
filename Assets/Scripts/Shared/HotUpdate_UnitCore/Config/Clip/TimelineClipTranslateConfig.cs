using System;
using UnityEngine;

[Serializable]
public class TimelineClipTranslateConfig : TimelineClipUnitConfigBase
{
    /// <summary>终点参考，默认本次施法对象。</summary>
    public TimelineMoveDestinationType Destination;
    /// <summary>终点取位移开始时最近对象或全部参考对象的中心。</summary>
    public TimelineMoveReferenceType ReferenceSelection;
    /// <summary>终点所引用的施法或效果绑定，空值取前方最近绑定。</summary>
    public string DestinationBindingKey;

    public DurationType DurationType;
    public float DurationPraram;
    public TimelineClipPointData PointData;
    /// <summary>终点偏移空间，默认 ApproachDirection；X 负值停在参考点前。</summary>
    public TimelineMoveOffsetSpace OffsetSpace;
    public bool IsRotateToAtStart;

    /// <summary>是否受角色定帧（HitStop）影响；默认 true。关闭后位移跟全局轴，条长不加定帧重叠。</summary>
    public bool AffectedByHitStop = true;
}
