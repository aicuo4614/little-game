using UnityEngine;

/// <summary>Scale Clip 共用计算（编辑器预览与运行时）。</summary>
public static class TimelineClipScaleUtil
{
    public static float ResolveClipDuration(TimelineClipScaleConfig cfg)
    {
        if (cfg.DurationType == DurationType.Time)
        {
            return cfg.DurationPraram;
        }

        var speed = Mathf.Max(cfg.DurationPraram, 0.01f);
        return Vector3.Distance(cfg.StartScale, cfg.EndScale) / speed;
    }
}
