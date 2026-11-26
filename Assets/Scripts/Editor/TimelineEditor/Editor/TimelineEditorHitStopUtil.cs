using System.Collections.Generic;
using UnityEngine;

/// <summary>技能编辑器侧 HitStop：收集单位定帧窗口、采样时间、互斥钳制。</summary>
public static class TimelineEditorHitStopUtil
{
    private static readonly List<TimelineClipHitStopUtil.FreezeWindow> WindowBuffer =
        new List<TimelineClipHitStopUtil.FreezeWindow>();

    /// <summary>收集对 <paramref name="unit"/> 生效的全部 HitStop 窗口。</summary>
    public static void CollectFreezeWindowsForUnit(
        TimelineControl control,
        UnitMono unit,
        List<TimelineClipHitStopUtil.FreezeWindow> results)
    {
        results.Clear();
        if (control == null || unit == null)
        {
            return;
        }

        var clips = control.Clips;
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            if (clip.Config is not TimelineClipHitStopConfig hitStopConfig)
            {
                continue;
            }

            var targets = control.UnitBindings.ResolveUnits(hitStopConfig, hitStopConfig.Subject, hitStopConfig.TargetBindingKey);
            if (!ContainsUnit(targets, unit))
            {
                continue;
            }

            var start = clip.GetContentTimelineStartTime();
            var end = clip.GetContentTimelineEndTime();
            if (end <= start)
            {
                continue;
            }

            results.Add(new TimelineClipHitStopUtil.FreezeWindow
            {
                Start = start,
                End = end,
            });
        }
    }

    /// <summary>收集时间轴上全部 HitStop 窗口（不按单位过滤，供相机震屏等全局表现）。</summary>
    public static void CollectAllFreezeWindows(
        TimelineControl control,
        List<TimelineClipHitStopUtil.FreezeWindow> results)
    {
        results.Clear();
        if (control == null)
        {
            return;
        }

        var clips = control.Clips;
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            if (clip.Config == null || clip.Config.Type != TimelineClipType.HitStop)
            {
                continue;
            }

            var start = clip.GetContentTimelineStartTime();
            var end = clip.GetContentTimelineEndTime();
            if (end <= start)
            {
                continue;
            }

            results.Add(new TimelineClipHitStopUtil.FreezeWindow
            {
                Start = start,
                End = end,
            });
        }
    }

    private static readonly List<TimelineClipHitStopUtil.FreezeWindow> ClippedWindowBuffer =
        new List<TimelineClipHitStopUtil.FreezeWindow>();

    /// <summary>
    /// Clip 内容区内的逻辑播放时间（扣除时间轴上任意 HitStop，不按单位）。
    /// 用于相机震屏等与「冻谁」查询无关的表现。
    /// </summary>
    public static float GetClipLogicPlayTimingForAnyHitStop(
        TimelineControl control,
        float contentStart,
        float runningTime)
    {
        CollectAllFreezeWindows(control, WindowBuffer);
        return TimelineClipHitStopUtil.GetElapsedExcludingFreeze(contentStart, runningTime, WindowBuffer);
    }

    /// <summary>
    /// 逻辑播放时间：定帧窗口先裁到逻辑内容区间 [contentStart, contentStart+rawDuration] 的交叉段，
    /// 只对交叉重叠暂停（与条长叠加规则一致）。
    /// </summary>
    public static float GetClipLogicPlayTimingForAnyHitStopClippedToRaw(
        TimelineControl control,
        float contentStart,
        float rawContentDuration,
        float runningTime)
    {
        CollectAllFreezeWindows(control, WindowBuffer);
        TimelineClipHitStopUtil.ClipWindowsToRange(
            WindowBuffer,
            contentStart,
            contentStart + Mathf.Max(rawContentDuration, 0f),
            ClippedWindowBuffer);
        return TimelineClipHitStopUtil.GetElapsedExcludingFreeze(contentStart, runningTime, ClippedWindowBuffer);
    }

    /// <summary>原始逻辑时长在时间轴上应展示的 Duration（= raw + 与逻辑窗口交叉的定帧）。</summary>
    public static float MeasureDisplayDurationForAnyHitStop(
        TimelineControl control,
        float contentStart,
        float rawContentDuration)
    {
        CollectAllFreezeWindows(control, WindowBuffer);
        return TimelineClipHitStopUtil.MeasureDisplayDuration(contentStart, rawContentDuration, WindowBuffer);
    }

    /// <summary>逻辑窗口与全部定帧的交叉重叠总时长。</summary>
    public static float GetOverlapWithAnyHitStop(
        TimelineControl control,
        float contentStart,
        float rawContentDuration)
    {
        CollectAllFreezeWindows(control, WindowBuffer);
        return TimelineClipHitStopUtil.GetFreezeOverlapDuration(
            contentStart,
            contentStart + Mathf.Max(rawContentDuration, 0f),
            WindowBuffer);
    }

    /// <summary>
    /// 从当前展示 Duration 反推原始逻辑时长，再写回含交叉定帧的展示 Duration。
    /// </summary>
    public static float SyncDisplayDurationForAnyHitStop(
        TimelineControl control,
        float contentStart,
        float currentDisplayDuration)
    {
        CollectAllFreezeWindows(control, WindowBuffer);
        var raw = TimelineClipHitStopUtil.SolveRawDurationFromDisplay(
            contentStart,
            currentDisplayDuration,
            WindowBuffer);
        return TimelineClipHitStopUtil.MeasureDisplayDuration(contentStart, raw, WindowBuffer);
    }

    /// <summary>原始逻辑时长（展示 Duration 按交叉重叠反推）。</summary>
    public static float GetRawDurationForAnyHitStop(
        TimelineControl control,
        float contentStart,
        float currentDisplayDuration)
    {
        CollectAllFreezeWindows(control, WindowBuffer);
        return TimelineClipHitStopUtil.SolveRawDurationFromDisplay(
            contentStart,
            currentDisplayDuration,
            WindowBuffer);
    }

    /// <summary>从 0 到 <paramref name="globalTime"/>，单位已流逝的逻辑时间。</summary>
    public static float GetUnitElapsedFromZero(TimelineControl control, UnitMono unit, float globalTime)
    {
        CollectFreezeWindowsForUnit(control, unit, WindowBuffer);
        return TimelineClipHitStopUtil.GetElapsedExcludingFreeze(0f, globalTime, WindowBuffer);
    }

    /// <summary>
    /// Clip 内容区内的逻辑播放时间（已扣除对该单位生效的定帧）。
    /// </summary>
    public static float GetClipLogicPlayTiming(
        TimelineControl control,
        UnitMono unit,
        float contentStart,
        float runningTime)
    {
        CollectFreezeWindowsForUnit(control, unit, WindowBuffer);
        return TimelineClipHitStopUtil.GetElapsedExcludingFreeze(contentStart, runningTime, WindowBuffer);
    }

    /// <summary>原始内容时长在时间轴上应展示的 Duration（含插入定帧）。</summary>
    public static float MeasureDisplayDurationForUnit(
        TimelineControl control,
        UnitMono unit,
        float contentStart,
        float rawContentDuration)
    {
        CollectFreezeWindowsForUnit(control, unit, WindowBuffer);
        return TimelineClipHitStopUtil.MeasureDisplayDuration(contentStart, rawContentDuration, WindowBuffer);
    }

    /// <summary>
    /// PlayHit 等多目标：取各目标展示时长的最大值，保证最慢被冻住的目标也能播完。
    /// </summary>
    public static float MeasureDisplayDurationForUnits(
        TimelineControl control,
        List<UnitMono> units,
        float contentStart,
        float rawContentDuration)
    {
        if (units == null || units.Count == 0)
        {
            return Mathf.Max(0f, rawContentDuration);
        }

        var maxDuration = 0f;
        for (var i = 0; i < units.Count; i++)
        {
            var d = MeasureDisplayDurationForUnit(control, units[i], contentStart, rawContentDuration);
            if (d > maxDuration)
            {
                maxDuration = d;
            }
        }

        return maxDuration;
    }

    private static bool ContainsUnit(List<UnitMono> list, UnitMono unit)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == unit)
            {
                return true;
            }
        }

        return false;
    }
}
