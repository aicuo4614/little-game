using System;

[Serializable]
public class TimelineClipRoleRotateToTargetConfig : TimelineClipUnitConfigBase
{
    /// <summary>朝向的参考角色类别，仅允许施法对象、效果来源或效果目标。</summary>
    public TimelineMoveDestinationType Destination;
    /// <summary>参考角色所引用的绑定 Clip。</summary>
    public string DestinationBindingKey;
    /// <summary>各单位分别选择最近参考对象，或朝向全部参考对象的中心。</summary>
    public TimelineMoveReferenceType ReferenceSelection;
}
