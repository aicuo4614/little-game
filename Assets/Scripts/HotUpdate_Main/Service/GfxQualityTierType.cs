/// <summary>
/// URP 画质档位，与 <see cref="UnityEngine.QualitySettings"/> 索引一一对应。
/// </summary>
public enum GfxQualityTierType
{
    /// <summary>Performant（索引 0）。低画质保流畅。</summary>
    Performant = 0,

    /// <summary>Balanced（索引 1）。默认档。</summary>
    Balanced = 1,

    /// <summary>High Fidelity（索引 2）。高画质。</summary>
    HighFidelity = 2,

    /// <summary>Ultra（索引 3）。最高画质。</summary>
    Ultra = 3,
}
