using System;
using System.Collections.Generic;

/// <summary>声明本次效果的来源和目标，配置不包含服务端 Action 类型。</summary>
[Serializable]
public sealed class TimelineClipSetHitTargetsConfig : TimelineClipConfigBase
{
    /// <summary>相对预览来源 $caster/$ally:n/$enemy:n；实战使用真实效果来源。</summary>
    public string PreviewSource = "$caster";
    /// <summary>相对预览目标 $caster/$ally:n/$enemy:n；实战使用真实效果目标。</summary>
    public List<string> PreviewTargets = new List<string>();
    public TimelineClipSetHitTargetsConfig()
    {
        Type = TimelineClipType.SetHitTargets;
    }
}
