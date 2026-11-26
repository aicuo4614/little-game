using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 技能编辑器资源路径：JSON 只存逻辑名（无 Assets/、无 Low|HQ、无 Base|OnDemand、无扩展名）。
/// 编辑器预览用 AssetDatabase 加载，画质解析对齐运行时 UnitLoadMgr / EffectMgr：当前档优先，HQ 缺则回落 Low；
/// 同画质下按 OnDemand → Base 试工程路径。
/// </summary>
public enum TimelinePreviewQualityType
{
    /// <summary>标准/低端（主内容）。</summary>
    Low = 0,

    /// <summary>高精度；缺资源时回落 Low。</summary>
    HQ = 1,
}

public static class EditorTimelineAssetPathUtil
{
    const string PreviewQualityPrefsKey = "TimelineEditor.PreviewQuality";

    public const string PackageUnitPrefix = "Assets/Package_Unit/";
    public const string PackageEffectPrefix = "Assets/Package_Effect/";
    public const string DefaultTimelineFolder = "Assets/Package_Unit/Timeline/Base";

    /// <summary>默认施法者逻辑名（相对 Package_Unit/{Low|HQ}/Base|OnDemand）。</summary>
    public const string DefaultUnitLogicalName = "Archer/Archer";

    /// <summary>新建 PlayEffect / PlayHit 默认特效逻辑名。</summary>
    public const string DefaultEffectLogicalName = "fx_pierce_flash";

    /// <summary>新建 PlayBullet 默认子弹逻辑名。</summary>
    public const string DefaultBulletLogicalName = "fx_arrow_pierce";

    /// <summary>兼容旧常量名：现为逻辑名。</summary>
    public const string DefaultUnitPrefabPath = DefaultUnitLogicalName;

    static TimelinePreviewQualityType previewQuality = TimelinePreviewQualityType.Low;
    static bool previewQualityLoaded;

    /// <summary>编辑器预览画质（不写 JSON）；HQ 缺资源时 Resolve 回落 Low。</summary>
    public static TimelinePreviewQualityType PreviewQuality
    {
        get
        {
            EnsurePreviewQualityLoaded();
            return previewQuality;
        }
        set
        {
            EnsurePreviewQualityLoaded();
            if (previewQuality == value)
            {
                return;
            }

            previewQuality = value;
            EditorPrefs.SetInt(PreviewQualityPrefsKey, (int)value);
        }
    }

    static void EnsurePreviewQualityLoaded()
    {
        if (previewQualityLoaded)
        {
            return;
        }

        previewQualityLoaded = true;
        var raw = EditorPrefs.GetInt(PreviewQualityPrefsKey, (int)TimelinePreviewQualityType.Low);
        previewQuality = raw == (int)TimelinePreviewQualityType.HQ
            ? TimelinePreviewQualityType.HQ
            : TimelinePreviewQualityType.Low;
    }

    /// <summary>
    /// 工程路径 / 旧完整路径 / 已是逻辑名 → 单位逻辑名。
    /// 例：<c>Assets/Package_Unit/Unit/Low/OnDemand/a/a.prefab</c> → <c>a/a</c>。
    /// </summary>
    public static string ToUnitLogicalName(string pathOrLogical)
    {
        if (string.IsNullOrEmpty(pathOrLogical))
        {
            return string.Empty;
        }

        var path = pathOrLogical.Replace('\\', '/');
        if (path.StartsWith(PackageUnitPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(PackageUnitPrefix.Length);
            if (path.StartsWith("Unit/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring("Unit/".Length);
            }

            path = StripQualityCollectFolders(path);
        }
        else if (path.StartsWith("Unit/", StringComparison.OrdinalIgnoreCase))
        {
            path = StripQualityCollectFolders(path.Substring("Unit/".Length));
        }

        path = path.Replace("Battle/Base/", "Battle/").Replace("Battle/OnDemand/", "Battle/");
        return StripExtension(path);
    }

    /// <summary>
    /// 工程路径 / 已是逻辑名 → 特效逻辑名。
    /// 例：<c>Assets/Package_Effect/Low/OnDemand/foo.prefab</c> → <c>foo</c>。
    /// </summary>
    public static string ToEffectLogicalName(string pathOrLogical)
    {
        if (string.IsNullOrEmpty(pathOrLogical))
        {
            return string.Empty;
        }

        var path = pathOrLogical.Replace('\\', '/');
        if (path.StartsWith(PackageEffectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = StripQualityCollectFolders(path.Substring(PackageEffectPrefix.Length));
        }
        else if (path.StartsWith("Effect/", StringComparison.OrdinalIgnoreCase))
        {
            path = StripQualityFolder(path.Substring("Effect/".Length));
        }

        return StripExtension(path);
    }

    /// <summary>逻辑名 → 可加载的单位 prefab 工程路径；找不到返回空。</summary>
    public static string ResolveUnitPrefabAssetPath(string logicalName)
    {
        var logical = ToUnitLogicalName(logicalName);
        if (string.IsNullOrEmpty(logical))
        {
            return string.Empty;
        }

        if (logical.StartsWith("Battle/", StringComparison.Ordinal))
        {
            var relative = logical.Substring("Battle/".Length);
            foreach (var layer in new[] { "Base", "OnDemand" })
            {
                var candidate = PackageUnitPrefix + "Unit/Battle/" + layer + "/" + relative + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(candidate))
                {
                    return candidate;
                }
            }
            return string.Empty;
        }

        var quality = PreviewQuality;
        var primary = FindUnitPrefabAssetPath(quality, logical);
        if (!string.IsNullOrEmpty(primary))
        {
            return primary;
        }

        if (quality == TimelinePreviewQualityType.Low)
        {
            return string.Empty;
        }

        return FindUnitPrefabAssetPath(TimelinePreviewQualityType.Low, logical);
    }

    /// <summary>逻辑名 → 可加载的特效 prefab 工程路径；找不到返回空。</summary>
    public static string ResolveEffectPrefabAssetPath(string logicalName)
    {
        var logical = ToEffectLogicalName(logicalName);
        if (string.IsNullOrEmpty(logical))
        {
            return string.Empty;
        }

        var quality = PreviewQuality;
        var primary = FindEffectPrefabAssetPath(quality, logical);
        if (!string.IsNullOrEmpty(primary))
        {
            return primary;
        }

        if (quality == TimelinePreviewQualityType.Low)
        {
            return string.Empty;
        }

        return FindEffectPrefabAssetPath(TimelinePreviewQualityType.Low, logical);
    }

    public static GameObject LoadUnitPrefab(string logicalName)
    {
        var assetPath = ResolveUnitPrefabAssetPath(logicalName);
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
    }

    public static GameObject LoadEffectPrefab(string logicalName)
    {
        var assetPath = ResolveEffectPrefabAssetPath(logicalName);
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
    }

    /// <summary>拖入单位 prefab 后写入 JSON 的逻辑名。</summary>
    public static string UnitLogicalFromAssetPath(string assetPath)
    {
        return ToUnitLogicalName(assetPath);
    }

    /// <summary>拖入特效 prefab 后写入 JSON 的逻辑名。</summary>
    public static string EffectLogicalFromAssetPath(string assetPath)
    {
        return ToEffectLogicalName(assetPath);
    }

    static string FindUnitPrefabAssetPath(TimelinePreviewQualityType quality, string logical)
    {
        var folder = quality == TimelinePreviewQualityType.HQ ? "HQ" : "Low";
        var candidates = new[]
        {
            PackageUnitPrefix + folder + "/OnDemand/" + logical + ".prefab",
            PackageUnitPrefix + folder + "/Base/" + logical + ".prefab",
            PackageUnitPrefix + folder + "/Base/" + logical + "/" + logical + ".prefab",
            PackageUnitPrefix + "Unit/" + folder + "/OnDemand/" + logical + ".prefab",
            PackageUnitPrefix + "Unit/" + folder + "/Base/" + logical + ".prefab",
        };
        for (var i = 0; i < candidates.Length; i++)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(candidates[i]) != null)
            {
                return candidates[i];
            }
        }

        return string.Empty;
    }

    static string FindEffectPrefabAssetPath(TimelinePreviewQualityType quality, string logical)
    {
        var folder = quality == TimelinePreviewQualityType.HQ ? "HQ" : "Low";
        var candidates = new[]
        {
            PackageEffectPrefix + folder + "/OnDemand/" + logical + ".prefab",
            PackageEffectPrefix + folder + "/Base/" + logical + ".prefab",
        };
        for (var i = 0; i < candidates.Length; i++)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(candidates[i]) != null)
            {
                return candidates[i];
            }
        }

        return string.Empty;
    }

    /// <summary><c>Low|HQ/Base|OnDemand/...</c> → 相对逻辑路径。</summary>
    static string StripQualityCollectFolders(string relativePath)
    {
        if (TryStripCollect(relativePath, "Low", out var lowRest))
        {
            return lowRest;
        }

        if (TryStripCollect(relativePath, "HQ", out var hqRest))
        {
            return hqRest;
        }

        if (relativePath.StartsWith("Base/", StringComparison.OrdinalIgnoreCase))
        {
            return relativePath.Substring("Base/".Length);
        }

        if (relativePath.StartsWith("OnDemand/", StringComparison.OrdinalIgnoreCase))
        {
            return relativePath.Substring("OnDemand/".Length);
        }

        return relativePath;
    }

    static bool TryStripCollect(string relativePath, string quality, out string rest)
    {
        var basePrefix = quality + "/Base/";
        if (relativePath.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
        {
            rest = relativePath.Substring(basePrefix.Length);
            return true;
        }

        var onDemandPrefix = quality + "/OnDemand/";
        if (relativePath.StartsWith(onDemandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            rest = relativePath.Substring(onDemandPrefix.Length);
            return true;
        }

        rest = relativePath;
        return false;
    }

    static string StripQualityFolder(string path)
    {
        if (path.StartsWith("Low/", StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring("Low/".Length);
        }

        if (path.StartsWith("HQ/", StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring("HQ/".Length);
        }

        return path;
    }

    static string StripExtension(string path)
    {
        if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring(0, path.Length - ".prefab".Length);
        }

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring(0, path.Length - ".json".Length);
        }

        return path;
    }
}
