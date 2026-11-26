using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HitStop 时间映射（编辑器预览与后续运行时共用）：
/// 全局时间继续走，角色逻辑时间在定帧窗口内暂停。
/// </summary>
public static class TimelineClipHitStopUtil
{
    /// <summary>一段对某单位生效的定帧窗口（时间轴全局秒）。</summary>
    public struct FreezeWindow
    {
        public float Start;
        public float End;

        public float Duration => Mathf.Max(0f, End - Start);
    }

    /// <summary>两段半开/闭区间是否时间重叠（端点相接不算重叠）。</summary>
    public static bool RangesOverlap(float aStart, float aEnd, float bStart, float bEnd)
    {
        return aStart < bEnd && bStart < aEnd;
    }

    /// <summary>
    /// 从 <paramref name="globalOrigin"/> 到 <paramref name="globalTime"/>，
    /// 扣除定帧窗口后的角色已流逝时间（可正可负，由调用方解释方向）。
    /// </summary>
    public static float GetElapsedExcludingFreeze(
        float globalOrigin,
        float globalTime,
        List<FreezeWindow> windows)
    {
        if (windows == null || windows.Count == 0)
        {
            return globalTime - globalOrigin;
        }

        if (globalTime >= globalOrigin)
        {
            return (globalTime - globalOrigin) - GetFreezeOverlapDuration(globalOrigin, globalTime, windows);
        }

        return (globalTime - globalOrigin) + GetFreezeOverlapDuration(globalTime, globalOrigin, windows);
    }

    /// <summary>区间 [<paramref name="rangeStart"/>, <paramref name="rangeEnd"/>] 与定帧窗口重叠的总时长。</summary>
    public static float GetFreezeOverlapDuration(
        float rangeStart,
        float rangeEnd,
        List<FreezeWindow> windows)
    {
        if (windows == null || windows.Count == 0 || rangeEnd <= rangeStart)
        {
            return 0f;
        }

        var total = 0f;
        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var start = Mathf.Max(rangeStart, w.Start);
            var end = Mathf.Min(rangeEnd, w.End);
            if (end > start)
            {
                total += end - start;
            }
        }

        return total;
    }

    /// <summary>
    /// 展示时长 = 逻辑内容时长 + 与逻辑窗口 [<paramref name="contentStart"/>, contentStart+raw] 交叉的定帧时长。
    /// 只加交叉重叠，不加定帧整段（定帧伸出逻辑窗口外的部分不计）。
    /// </summary>
    public static float MeasureDisplayDuration(
        float contentStart,
        float rawContentDuration,
        List<FreezeWindow> windows)
    {
        var raw = Mathf.Max(0f, rawContentDuration);
        if (raw <= 0f)
        {
            return 0f;
        }

        return raw + GetFreezeOverlapDuration(contentStart, contentStart + raw, windows);
    }

    /// <summary>
    /// 从展示时长反推逻辑时长（满足 display ≈ raw + overlap(start, start+raw)）。
    /// </summary>
    public static float SolveRawDurationFromDisplay(
        float contentStart,
        float displayDuration,
        List<FreezeWindow> windows)
    {
        var display = Mathf.Max(0f, displayDuration);
        if (windows == null || windows.Count == 0)
        {
            return display;
        }

        var raw = display;
        for (var i = 0; i < 8; i++)
        {
            var overlap = GetFreezeOverlapDuration(contentStart, contentStart + raw, windows);
            var next = Mathf.Max(0f, display - overlap);
            if (Mathf.Abs(next - raw) <= 1e-5f)
            {
                return next;
            }

            raw = next;
        }

        return raw;
    }

    /// <summary>把定帧窗口裁到 [<paramref name="rangeStart"/>, <paramref name="rangeEnd"/>] 内（只保留交叉段）。</summary>
    public static void ClipWindowsToRange(
        List<FreezeWindow> windows,
        float rangeStart,
        float rangeEnd,
        List<FreezeWindow> results)
    {
        results.Clear();
        if (windows == null || rangeEnd <= rangeStart)
        {
            return;
        }

        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var start = Mathf.Max(rangeStart, w.Start);
            var end = Mathf.Min(rangeEnd, w.End);
            if (end > start)
            {
                results.Add(new FreezeWindow
                {
                    Start = start,
                    End = end,
                });
            }
        }
    }
}
