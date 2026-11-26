/// <summary>
/// 画质档位存档与当前值。Demo 精简：优先读本地 Pref；无存档默认 Balanced。
/// 实际 <see cref="QualitySettings"/> 应用见 <see cref="GfxQualityMgr"/>。
/// </summary>
public sealed class QualityTierMgr
{
    const string LogTag = "QualityTier";

    /// <summary>本地存档键：用户选定的画质档（int，对齐 <see cref="GfxQualityTierType"/>）。</summary>
    public const string PreferredTierPrefsKey = "GfxQuality.PreferredTier";

    static QualityTierMgr instance;
    GfxQualityTierType currentTier;
    bool initialized;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    /// <summary>单例。</summary>
    public static QualityTierMgr Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new QualityTierMgr();
            }

            return instance;
        }
    }

    /// <summary>当前档位。</summary>
    public GfxQualityTierType CurrentTier => currentTier;

    /// <summary>是否已 Init。</summary>
    public bool HasInitialized => initialized;

    /// <summary>读 Pref 或默认 Balanced。幂等。</summary>
    public void Init()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        if (TryLoadPreferredTier(out var savedTier))
        {
            currentTier = savedTier;
            GameDebug.Log(LogTag, $"Tier={currentTier} from LocalSave");
            return;
        }

        currentTier = GfxQualityTierType.Balanced;
        SavePreferredTier(currentTier);
        GameDebug.Log(LogTag, $"Tier={currentTier} default=Balanced，已写入本地存档");
    }

    /// <summary>
    /// 设置窗手动选档：写本地存档、更新 <see cref="CurrentTier"/>，并立刻经 <see cref="GfxQualityMgr"/> 应用。
    /// </summary>
    public void SetPreferredTier(GfxQualityTierType tier)
    {
        SavePreferredTier(tier);
        currentTier = tier;
        GfxQualityMgr.Instance.SetTier(tier);
        GameDebug.Log(LogTag, $"Tier={tier} 已由设置写入本地存档并应用");
    }

    static bool TryLoadPreferredTier(out GfxQualityTierType tier)
    {
        if (!MVC.LocalSaveService.HasKey(PreferredTierPrefsKey))
        {
            tier = GfxQualityTierType.Balanced;
            return false;
        }

        var raw = MVC.LocalSaveService.GetInt(PreferredTierPrefsKey, (int)GfxQualityTierType.Balanced);
        if (raw < (int)GfxQualityTierType.Performant || raw > (int)GfxQualityTierType.Ultra)
        {
            tier = GfxQualityTierType.Balanced;
            return false;
        }

        tier = (GfxQualityTierType)raw;
        return true;
    }

    static void SavePreferredTier(GfxQualityTierType tier)
    {
        MVC.LocalSaveService.SetInt(PreferredTierPrefsKey, (int)tier);
    }
}
