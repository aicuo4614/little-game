using System;

/// <summary>
/// 特效点光时间轴采样关键帧：绝对时间（秒）上的 Intensity / Range / Color。
/// </summary>
[Serializable]
public class EffectPointLightSample
{
    /// <summary>采样时刻（秒，相对特效开始）。</summary>
    public float Time;

    /// <summary>该时刻 Intensity。</summary>
    public float Intensity;

    /// <summary>该时刻 Range。</summary>
    public float Range;

    /// <summary>该时刻颜色 RGBA。</summary>
    public float ColorR = 1f;
    public float ColorG = 1f;
    public float ColorB = 1f;
    public float ColorA = 1f;
}
