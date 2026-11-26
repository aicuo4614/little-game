using System.IO;
using YooAsset.Editor;

/// <summary>
/// Yoo 地址 = 相对采集根目录的路径、无扩展名、正斜杠。
/// <c>Package_Res</c> / <c>Package_Unit</c> / <c>Package_Effect</c> / <c>Package_Scene</c> 固定相对各自包根。
/// Effect / Scene：<c>{Low|HQ}/Base|OnDemand</c> → 地址 <c>Effect|Scene/Low|HQ/...</c>。
/// Unit：<c>Unit/{Low|HQ}/Base|OnDemand</c> → <c>Unit/Low|HQ/...</c>；
/// <c>Unit/Battle/Base|OnDemand</c>、Skill、Timeline 剥掉 Base|OnDemand，地址不含这层。
/// </summary>
public class AddressByBundleAndRelativePath : IAddressRule
{
    const string PackageResRoot = "Assets/Package_Res";
    const string PackageUnitRoot = "Assets/Package_Unit";
    const string PackageEffectRoot = "Assets/Package_Effect";
    const string PackageSceneRoot = "Assets/Package_Scene";

    string IAddressRule.GetAssetAddress(AddressRuleData data)
    {
        var assetPath = data.AssetPath.Replace('\\', '/');
        string relativePath;
        if (assetPath.StartsWith(PackageEffectRoot + "/"))
        {
            relativePath = assetPath[(PackageEffectRoot.Length + 1)..];
            relativePath = StripNamedQualityCollectLayer(relativePath, "Effect");
            return Path.ChangeExtension(relativePath, null).Replace('\\', '/');
        }

        if (assetPath.StartsWith(PackageSceneRoot + "/"))
        {
            relativePath = assetPath[(PackageSceneRoot.Length + 1)..];
            relativePath = StripNamedQualityCollectLayer(relativePath, "Scene");
            return Path.ChangeExtension(relativePath, null).Replace('\\', '/');
        }

        if (assetPath.StartsWith(PackageUnitRoot + "/"))
        {
            relativePath = assetPath[(PackageUnitRoot.Length + 1)..];
            if (relativePath.StartsWith("Low/") || relativePath.StartsWith("HQ/"))
                return Path.ChangeExtension(StripNamedQualityCollectLayer(relativePath, "Unit"), null).Replace('\\', '/');
        }
        else if (assetPath.StartsWith(PackageResRoot + "/"))
        {
            relativePath = assetPath[(PackageResRoot.Length + 1)..];
        }
        else
        {
            relativePath = data.AssetPath[(data.CollectPath.Length + 1)..];
        }

        relativePath = StripCollectLayer(relativePath);
        return Path.ChangeExtension(relativePath, null).Replace('\\', '/');
    }

    /// <summary>
    /// <c>{Low|HQ}/Base|OnDemand/...</c> → <c>{addressPrefix}/{Low|HQ}/...</c>。
    /// 旧根级 <c>Base|OnDemand</c> 视为 Low。
    /// </summary>
    static string StripNamedQualityCollectLayer(string relativePath, string addressPrefix)
    {
        if (TryStripNamedQualityLayer(relativePath, addressPrefix, "Low", out var lowPath))
        {
            return lowPath;
        }

        if (TryStripNamedQualityLayer(relativePath, addressPrefix, "HQ", out var hqPath))
        {
            return hqPath;
        }

        if (relativePath.StartsWith("Base/"))
        {
            return addressPrefix + "/Low/" + relativePath["Base/".Length..];
        }

        if (relativePath.StartsWith("OnDemand/"))
        {
            return addressPrefix + "/Low/" + relativePath["OnDemand/".Length..];
        }

        if (relativePath.StartsWith(addressPrefix + "/"))
        {
            return relativePath;
        }

        return addressPrefix + "/Low/" + relativePath;
    }

    /// <summary><c>{quality}/Base|OnDemand/...</c> → <c>{addressPrefix}/{quality}/...</c>。</summary>
    static bool TryStripNamedQualityLayer(
        string relativePath,
        string addressPrefix,
        string quality,
        out string address)
    {
        var basePrefix = quality + "/Base/";
        if (relativePath.StartsWith(basePrefix))
        {
            address = addressPrefix + "/" + quality + "/" + relativePath[basePrefix.Length..];
            return true;
        }

        var onDemandPrefix = quality + "/OnDemand/";
        if (relativePath.StartsWith(onDemandPrefix))
        {
            address = addressPrefix + "/" + quality + "/" + relativePath[onDemandPrefix.Length..];
            return true;
        }

        address = relativePath;
        return false;
    }

    /// <summary>
    /// 去掉按需采集分层。
    /// 根目录 <c>Package_Unit/Base</c>（原 Global）不剥，地址仍为 <c>Base/...</c>。
    /// 旧 <c>Package_Res/Scene/Base|OnDemand</c> 仍剥成 <c>Scene/...</c>（迁移前兼容）。
    /// </summary>
    static string StripCollectLayer(string relativePath)
    {
        if (relativePath.StartsWith("Scene/Base/"))
        {
            return "Scene/Low/" + relativePath["Scene/Base/".Length..];
        }

        if (relativePath.StartsWith("Scene/OnDemand/"))
        {
            return "Scene/Low/" + relativePath["Scene/OnDemand/".Length..];
        }

        if (TryStripUnitQualityCollectLayer(relativePath, out var unitPath))
        {
            return unitPath;
        }

        // 战斗单位没有 Low/HQ。Base 与 OnDemand 只区分进包时机，地址仍是 Unit/Battle/...
        if (TryStripTypeCollectLayer(relativePath, "Unit/Battle", out var battlePath))
        {
            return battlePath;
        }

        if (TryStripTypeCollectLayer(relativePath, "Skill", out var skillPath))
        {
            return skillPath;
        }

        if (TryStripTypeCollectLayer(relativePath, "Timeline", out var timelinePath))
        {
            return timelinePath;
        }

        return relativePath;
    }

    /// <summary>
    /// <c>Unit/{Low|HQ}/Base|OnDemand/...</c> → <c>Unit/{Low|HQ}/...</c>；
    /// 旧 <c>Unit/Base|OnDemand/...</c> 视为 Low。
    /// </summary>
    static bool TryStripUnitQualityCollectLayer(string relativePath, out string address)
    {
        if (TryStripUnitQualityLayer(relativePath, "Low", out address))
        {
            return true;
        }

        if (TryStripUnitQualityLayer(relativePath, "HQ", out address))
        {
            return true;
        }

        if (relativePath.StartsWith("Unit/Base/"))
        {
            address = "Unit/Low/" + relativePath["Unit/Base/".Length..];
            return true;
        }

        if (relativePath.StartsWith("Unit/OnDemand/"))
        {
            address = "Unit/Low/" + relativePath["Unit/OnDemand/".Length..];
            return true;
        }

        address = relativePath;
        return false;
    }

    /// <summary><c>Unit/{quality}/Base|OnDemand/...</c> → <c>Unit/{quality}/...</c>。</summary>
    static bool TryStripUnitQualityLayer(string relativePath, string quality, out string address)
    {
        var basePrefix = "Unit/" + quality + "/Base/";
        if (relativePath.StartsWith(basePrefix))
        {
            address = "Unit/" + quality + "/" + relativePath[basePrefix.Length..];
            return true;
        }

        var onDemandPrefix = "Unit/" + quality + "/OnDemand/";
        if (relativePath.StartsWith(onDemandPrefix))
        {
            address = "Unit/" + quality + "/" + relativePath[onDemandPrefix.Length..];
            return true;
        }

        address = relativePath;
        return false;
    }

    /// <summary>将 <c>{type}/Base|OnDemand/...</c> 收成 <c>{type}/...</c>。</summary>
    static bool TryStripTypeCollectLayer(string relativePath, string typeFolder, out string stripped)
    {
        var basePrefix = typeFolder + "/Base/";
        if (relativePath.StartsWith(basePrefix))
        {
            stripped = typeFolder + "/" + relativePath[basePrefix.Length..];
            return true;
        }

        var onDemandPrefix = typeFolder + "/OnDemand/";
        if (relativePath.StartsWith(onDemandPrefix))
        {
            stripped = typeFolder + "/" + relativePath[onDemandPrefix.Length..];
            return true;
        }

        stripped = relativePath;
        return false;
    }
}
