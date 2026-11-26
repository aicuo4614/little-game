using UnityEngine;

/// <summary>CameraShake Clip 共用逻辑（编辑器预览与运行时）：幅度包络求值与应用到 ShakeTrans。</summary>
public static class TimelineClipCameraShakeUtil
{
    /// <summary>
    /// 按归一化时间（0~1）求包络倍率。DOTween 内部一律关 fadeOut，由本倍率控制衰减/增强。
    /// </summary>
    public static float EvaluateEnvelope(TimelineCameraShakeEnvelopeType envelopeType, float normalizedTime)
    {
        var t = Mathf.Clamp01(normalizedTime);
        switch (envelopeType)
        {
            case TimelineCameraShakeEnvelopeType.Constant:
                return 1f;
            case TimelineCameraShakeEnvelopeType.FadeIn:
                return t;
            case TimelineCameraShakeEnvelopeType.FadeInOut:
                // 三角包络：0→1→0，中间最猛
                return 1f - Mathf.Abs(2f * t - 1f);
            case TimelineCameraShakeEnvelopeType.FadeOut:
            default:
                return 1f - t;
        }
    }

    /// <summary>
    /// 将当前 ShakeTrans 相对起始位姿的偏移按包络缩放。须在 DOShake Goto/采样之后调用。
    /// </summary>
    public static void ApplyEnvelope(
        Transform shakeTrans,
        Vector3 startLocalPos,
        Quaternion startLocalRot,
        float envelope)
    {
        var pos = shakeTrans.localPosition;
        shakeTrans.localPosition = startLocalPos + (pos - startLocalPos) * envelope;

        var rot = shakeTrans.localRotation;
        shakeTrans.localRotation = Quaternion.SlerpUnclamped(startLocalRot, rot, envelope);
    }
}
