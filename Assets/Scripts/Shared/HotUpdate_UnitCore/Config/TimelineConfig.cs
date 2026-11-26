using System;
using System.Collections.Generic;

/// <summary>技能时间轴配置，预览与实战统一由 <see cref="TimelinePlayback"/> 消费。</summary>
[Serializable]
public class TimelineConfig
{
    public string TimelineId;
    /// <summary>编辑器备注，仅存 JSON，运行时不消费。</summary>
    public string Description;
    /// <summary>编辑器预览主角的单位逻辑名；空值使用乖乖虎，运行时不消费。</summary>
    public string PreviewCasterAddress;
    public string DurationBindClipKey;
    public List<TimelineClipConfigBase> Clips = new List<TimelineClipConfigBase>();
}
