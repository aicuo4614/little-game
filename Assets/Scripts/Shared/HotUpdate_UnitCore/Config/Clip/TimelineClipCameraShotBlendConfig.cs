using System;
using DG.Tweening;

/// <summary>
/// 相机运镜过渡：上一条运镜模板终点（或默认机位）→ 下一条 <see cref="TimelineClipType.CameraShot"/> 模板起点。
/// </summary>
[Serializable]
public class TimelineClipCameraShotBlendConfig : TimelineClipConfigBase
{
    /// <summary>
    /// 目标运镜 ClipKey。空则按时间自动找下一条 CameraShot
    /// （优先 ContentStart &gt;= 本条结束，否则 ContentStart &gt; 本条开始）。
    /// </summary>
    public string NextCameraShotClipKey = "";

    /// <summary>过渡缓动。</summary>
    public Ease BlendEase = Ease.InOutSine;

    /// <summary>
    /// 是否把 FOV 也插到目标运镜模板起点的 FOV。
    /// 目标未开 AnimateFov 时本开关无效（不改 FOV）。
    /// </summary>
    public bool BlendFov = true;
}
