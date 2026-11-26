using System;
using UnityEngine;

/// <summary>
/// PlayBullet 延迟移动段的缩放关键帧：本段起点缩放 + 本段持续时长（秒）。
/// 列表末段结束后接到起点 <c>OffsetScale</c>。
/// </summary>
[Serializable]
public class TimelineBulletDelayScaleKey
{
    /// <summary>本段起始缩放（XYZ）。</summary>
    public Vector3 Scale;

    /// <summary>从本关键帧插值到下一关键帧（或最终「缩放」）所花的时间（秒）。</summary>
    public float Duration;
}
