using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TimelineEventConfigBase
{
    public string EventKey;
    public TimelineEventType TimelineEventType;
    public ValueType ValueType;
    public float Time;
    public string EventName;

    /// <summary>Clip 内的绝对时间（秒）。Percent 时为 Time * duration。</summary>
    public float GetAbsoluteTime(float clipDuration)
    {
        if (ValueType == ValueType.Percent)
        {
            return Mathf.Clamp01(Time) * clipDuration;
        }

        return Time;
    }

    /// <summary>按 ValueType 写回 Time：Normal 存秒，Percent 存 0~1 比例。</summary>
    public void SetAbsoluteTime(float absoluteTime, float clipDuration)
    {
        absoluteTime = Mathf.Clamp(absoluteTime, 0f, clipDuration);
        if (ValueType == ValueType.Percent)
        {
            Time = clipDuration > 0f ? absoluteTime / clipDuration : 0f;
            return;
        }

        Time = absoluteTime;
    }

    public void ClampStoredTime(float clipDuration)
    {
        if (ValueType == ValueType.Percent)
        {
            Time = Mathf.Clamp01(Time);
            return;
        }

        Time = Mathf.Clamp(Time, 0f, clipDuration);
    }
}

public enum TimelineEventType
{
    CustomEvent,
    AnimationEvent = 1,
}