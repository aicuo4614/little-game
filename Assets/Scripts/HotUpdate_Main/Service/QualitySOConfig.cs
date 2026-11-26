using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 画质运行时配置（ModulePackage）。
/// URP Asset × 4 仍是静态「档位模板」；本 SO 管每档元数据与 FPS 等运行时项。
/// YooAsset 地址 <c>SOConfig/QualitySOConfig</c>。
/// </summary>
[CreateAssetMenu(fileName = "QualitySOConfig", menuName = "Kabu/SOConfig/QualitySOConfig")]
public class QualitySOConfig : ScriptableObject
{
    /// <summary>YooAsset 地址（无扩展名）。</summary>
    public const string AssetLocation = "SOConfig/QualitySOConfig";

    /// <summary>四档配置，与 <see cref="GfxQualityTierType"/> 索引对齐。</summary>
    public List<GfxQualityTierEntry> Tiers = new List<GfxQualityTierEntry>();

    /// <summary>
    /// 全局目标帧率，写入 <see cref="Application.targetFrameRate"/>。
    /// 单档 <see cref="GfxQualityTierEntry.TargetFrameRate"/> 非负时优先于本字段。
    /// </summary>
    public int TargetFrameRate = 60;

    /// <summary>是否在左上角常显 FPS 浮层。</summary>
    public bool ShowFps;

    /// <summary>按档位枚举查条目；找不到返回 null。</summary>
    public GfxQualityTierEntry FindTier(GfxQualityTierType tier)
    {
        for (var i = 0; i < Tiers.Count; i++)
        {
            var entry = Tiers[i];
            if (entry.Tier == tier)
            {
                return entry;
            }
        }

        return null;
    }
}
