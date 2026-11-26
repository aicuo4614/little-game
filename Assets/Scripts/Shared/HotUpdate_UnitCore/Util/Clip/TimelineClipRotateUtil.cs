using UnityEngine;

/// <summary>Rotate Clip 共用计算（编辑器预览与运行时）。</summary>
public static class TimelineClipRotateUtil
{
    public static Quaternion ResolveRotation(Quaternion originRotation, Vector3 eulerOffset)
    {
        return originRotation * Quaternion.Euler(eulerOffset);
    }

    public static float ResolveClipDuration(TimelineClipRotateConfig cfg, Quaternion originRotation)
    {
        if (cfg.DurationType == DurationType.Time)
        {
            return cfg.DurationPraram;
        }

        var speed = Mathf.Max(cfg.DurationPraram, 0.01f);
        var angle = Quaternion.Angle(
            ResolveRotation(originRotation, cfg.StartEuler),
            ResolveRotation(originRotation, cfg.EndEuler));
        return angle / speed;
    }
}
