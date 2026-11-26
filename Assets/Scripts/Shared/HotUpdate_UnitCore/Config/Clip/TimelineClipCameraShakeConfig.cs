using System;
using UnityEngine;

[Serializable]
public class TimelineClipCameraShakeConfig : TimelineClipConfigBase
{
    /// <summary>位置抖动幅度（本地空间）。</summary>
    public Vector3 PositionStrength = new Vector3(0.3f, 0.3f, 0f);

    /// <summary>旋转抖动幅度（欧拉角）。</summary>
    public Vector3 RotationStrength = Vector3.zero;

    /// <summary>震动次数(Vibrato)：持续时间内来回抖几次，越大抖得越密。</summary>
    public int Vibrato = 10;

    /// <summary>随机度(Randomness)：抖动方向的随机程度，0~180，越大方向越乱。</summary>
    public float Randomness = 90f;

    /// <summary>
    /// 幅度包络。旧 JSON 缺字段为 <see cref="TimelineCameraShakeEnvelopeType.FadeOut"/>（衰减）。
    /// </summary>
    public TimelineCameraShakeEnvelopeType EnvelopeType;
}
