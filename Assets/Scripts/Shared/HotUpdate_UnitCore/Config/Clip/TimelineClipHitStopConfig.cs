using System;

/// <summary>角色定帧 Clip 配置：按绑定暂停单位的局部时间，时长为定帧秒数。</summary>
[Serializable]
public class TimelineClipHitStopConfig : TimelineClipConfigBase
{
    /// <summary>局部时间暂停的单位；绑定使用 TargetBindingKey。</summary>
    public TimelineUnitSubjectType Subject = TimelineUnitSubjectType.Caster;
}
