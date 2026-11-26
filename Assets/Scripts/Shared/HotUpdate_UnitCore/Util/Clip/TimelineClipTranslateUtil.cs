using UnityEngine;

/// <summary>Translate Clip 共用计算（编辑器预览与运行时）。</summary>
public static class TimelineClipTranslateUtil
{
    public static float ResolveClipDuration(TimelineClipTranslateConfig cfg, float moveDistance)
    {
        if (cfg.DurationType == DurationType.Time)
        {
            return cfg.DurationPraram;
        }

        var speed = Mathf.Max(cfg.DurationPraram, 0.01f);
        return moveDistance / speed;
    }
}
