using System;

/// <summary>施法者的 Spine 动画，片段时长由时间轴配置指定。</summary>
[Serializable]
public sealed class TimelineClipPlaySpineConfig : TimelineClipConfigBase
{
    /// <summary>Spine 骨架数据中配置的动画名。</summary>
    public string AnimationName;

    /// <summary>动画采样速度。1 表示原速。</summary>
    public float Speed = 1f;

    /// <summary>为 true 时，在片段内循环播放。</summary>
    public bool Loop;

    public TimelineClipPlaySpineConfig()
    {
        Type = TimelineClipType.PlaySpine;
    }
}
