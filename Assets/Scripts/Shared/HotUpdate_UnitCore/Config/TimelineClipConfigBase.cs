using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>各 Feature 配置基类，供 JSON / 时间轴序列化。</summary>
[Serializable]
public class TimelineClipConfigBase
{
    /// <summary>全局唯一 Feature 标识，首次创建后写入 JSON 且不再变更。</summary>
    public string ClipKey;
    /// <summary>前置依赖的 ClipKey 或 EventKey（空字符串表示无前置）。</summary>
    public string PreKey = string.Empty;
    /// <summary>对象绑定 ClipKey。空值使用当前片段之前最近的对应绑定。</summary>
    public string TargetBindingKey;
    /// <summary>绑定另一个 Clip 的 ClipKey；该 Clip 结束时，本 Clip 也结束（不再按 Duration 结束）。</summary>
    public string EndByOtherClipEnd;
    public TimelineClipType Type;
    public float StartTime;
    /// <summary>相对 <see cref="StartTime"/> 的延迟时长，实际播放起点为 StartTime + DelayTime。</summary>
    public float DelayTime;
    public float Duration;

    public List<TimelineEventConfigBase> EventConfigs;
}
