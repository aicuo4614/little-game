using System;
using System.Collections.Generic;

[Serializable]
public class TimelineClipPlayAnimatorConfig : TimelineClipConfigBase
{
    public string PrefabPath;
    public string ParamName;

    /// <summary>是否受角色定帧（HitStop）影响；默认 true。关闭后动画跟全局轴，条长不加定帧重叠。</summary>
    public bool AffectedByHitStop = true;

    /// <summary>
    /// Animator 状态 Speed（编辑器从 Controller 写入）。
    /// 负数表示倒放。条长 = Clip.length / |Speed|。
    /// 负 Speed 时 Unity <c>Animator.Play</c> 会把 length 写成 Infinity，编辑器与运行时都改用
    /// <c>AnimationClip.SampleAnimation</c> 从尾扫到头，不要依赖 <c>normalizedTime=1</c> 起播。
    /// </summary>
    public float AnimatorStateSpeed = 1f;
}
