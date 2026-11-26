using UnityEngine;

/// <summary>SceneDarken Clip 共用计算（编辑器预览与运行时）。</summary>
public static class TimelineClipSceneDarkenUtil
{
    public static float EvaluateStrength(TimelineClipSceneDarkenConfig cfg, float localTime, float duration)
    {
        var clipDuration = Mathf.Max(duration, 0f);
        if (clipDuration <= 0f)
        {
            return cfg.EndStrength;
        }

        var t = Mathf.Clamp01(localTime / clipDuration);
        return Mathf.Lerp(cfg.StartStrength, cfg.EndStrength, t);
    }
}
