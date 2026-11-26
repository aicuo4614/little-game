using System;

/// <summary>
/// <see cref="QualitySOConfig"/> 中单档配置，与 <see cref="GfxQualityTierType"/> 一一对应。
/// Demo 精简版：只管显示名与目标帧率；URP 细节由 QualitySettings 四档 Asset 决定。
/// </summary>
[Serializable]
public class GfxQualityTierEntry
{
    /// <summary>逻辑档位，索引与 <see cref="UnityEngine.QualitySettings"/> 一致。</summary>
    public GfxQualityTierType Tier;

    /// <summary>设置界面 / 日志显示名。</summary>
    public string DisplayName;

    /// <summary>是否出现在用户画质设置列表。</summary>
    public bool VisibleInSettings = true;

    /// <summary>
    /// 目标帧率；<c>-1</c> 表示使用 <see cref="QualitySOConfig.TargetFrameRate"/>。
    /// </summary>
    public int TargetFrameRate = -1;
}
