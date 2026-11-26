using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 画质应用：读 <see cref="QualitySOConfig"/>，档位由 <see cref="QualityTierMgr"/> 提供。
/// Demo 精简：只切 <see cref="QualitySettings"/>、目标帧率与 FPS 浮层；不做 AP / Beautify AA。
/// </summary>
public sealed class GfxQualityMgr
{
    const string LogTag = "GfxQuality";

    static GfxQualityMgr instance;
    QualitySOConfig config;
    GfxQualityTierType currentTier;
    bool initialized;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    /// <summary>单例。</summary>
    public static GfxQualityMgr Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new GfxQualityMgr();
            }

            return instance;
        }
    }

    /// <summary>当前已应用的档位。</summary>
    public GfxQualityTierType CurrentTier => currentTier;

    /// <summary>是否已完成 Init 并具备配置。</summary>
    public bool HasInitialized => initialized;

    /// <summary>
    /// 从 ModulePackage 加载画质 SO，按 <see cref="QualityTierMgr.CurrentTier"/> 应用。
    /// 须在 <see cref="QualityTierMgr.Init"/> 与 ModulePackage 就绪之后调用。幂等。
    /// </summary>
    public async Task InitAsync()
    {
        if (initialized)
        {
            return;
        }

        config = await MVC.SOConfigService.GetAsync<QualitySOConfig>(
            QualitySOConfig.AssetLocation, ResourcePackageName.ModulePackage);
        initialized = true;
        currentTier = QualityTierMgr.Instance.CurrentTier;
        ApplyTier(currentTier);
        LogApplyResult();
    }

    /// <summary>手动切换档位并重新应用（不写本地存档；设置窗请走 <see cref="QualityTierMgr.SetPreferredTier"/>）。</summary>
    public void SetTier(GfxQualityTierType tier)
    {
        if (!initialized)
        {
            return;
        }

        currentTier = tier;
        ApplyTier(tier);
        LogApplyResult();
    }

    void ApplyTier(GfxQualityTierType tier)
    {
        var entry = config.FindTier(tier);
        // 本工程沿用 Unity 六档名称，Demo 四个逻辑档位映射到实际档位。
        int[] levels = { 0, 1, Mathf.Max(0, QualitySettings.names.Length - 2), Mathf.Max(0, QualitySettings.names.Length - 1) };
        QualitySettings.SetQualityLevel(levels[(int)tier], applyExpensiveChanges: true);
        QualitySettings.vSyncCount = 0;

        var targetFrameRate = entry.TargetFrameRate >= 0
            ? entry.TargetFrameRate
            : config.TargetFrameRate;
        Application.targetFrameRate = targetFrameRate;
        GameFpsDisplay.Ensure(config.ShowFps);
    }

    void LogApplyResult()
    {
        var entry = config.FindTier(currentTier);
        GameDebug.Log(
            LogTag,
            $"Tier={currentTier}({entry.DisplayName}) QualityLevel={QualitySettings.GetQualityLevel()} " +
            $"TargetFps={Application.targetFrameRate} ShowFps={config.ShowFps}");
    }
}
