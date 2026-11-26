using System;
using System.Collections.Generic;

/// <summary>声明本次施法对象绑定，预览名单只供美术编辑器模拟。</summary>
[Serializable]
public sealed class TimelineClipSetCastTargetsConfig : TimelineClipConfigBase
{
    /// <summary>编辑器相对角色标识 $caster/$ally:n/$enemy:n。实战使用外部注入的施法对象。</summary>
    public List<string> PreviewTargets = new List<string>();
    public TimelineClipSetCastTargetsConfig()
    {
        Type = TimelineClipType.SetCastTargets;
    }
}
