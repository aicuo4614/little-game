using System;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using YooAsset.Editor;

/// <summary>单工程资源包与热更程序集的可重复配置入口。</summary>
public static class LittleGameStackSetup
{
    [MenuItem("Little Game/Framework/Configure Stack")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play");
        var setting = AssetBundleCollectorSettingData.Setting;
        setting.UniqueBundleName = true;
        SetPackage(setting, "LaunchPackage", "Assets/Package_Launch");
        SetPackage(setting, "CodePackage", "Assets/Package_Code");
        SetPackage(setting, "MainPackage", "Assets/Package_Main");
        SetPackage(setting, "ModulePackage", "Assets/Package_Modules");
        SetPackage(setting, "UnitPackage", "Assets/Package_Unit");
        SetPackage(setting, "EffectPackage", "Assets/Package_Effect");
        SetPackage(setting, "ScenePackage", "Assets/Scenes");
        EditorUtility.SetDirty(setting);

        var hybrid = SettingsUtil.HybridCLRSettings;
        hybrid.hotUpdateAssemblyDefinitions = new[]
        {
            "Assets/LittleGame/Client/Domain/LittleGame.Domain.asmdef",
            "Assets/Scripts/Shared/HotUpdate_UnitCore/HotUpdate_UnitCore.asmdef",
            "Assets/Scripts/HotUpdate_Main/HotUpdate_Main.asmdef",
            "Assets/LittleGame/Client/LittleGame.Client.asmdef"
        }.Select(AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>).ToArray();
        hybrid.hotUpdateAssemblies = Array.Empty<string>();
        hybrid.patchAOTAssemblies = Array.Empty<string>();
        HybridCLR.Editor.Settings.HybridCLRSettings.Save();

        Directory.CreateDirectory("Assets/LittleGame/Resources/LaunchConfig");
        const string configPath = "Assets/LittleGame/Resources/LaunchConfig/LaunchSOConfig.asset";
        if (!AssetDatabase.LoadAssetAtPath<LaunchSOConfig>(configPath))
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<LaunchSOConfig>(), configPath);
        File.WriteAllText("Assets/Package_Launch/bootstrap.json", "{\"schema\":1,\"entry\":\"Combat\"}");
        File.WriteAllText("Assets/Package_Main/content.json", "{\"schema\":1}");
        File.WriteAllText("Assets/Package_Modules/modules.json", "{\"Battle\":\"LittleGame.Client.Battle.BattleModule\"}");
        // Editor 模拟需要非空 CodePackage；构建前还必须执行真实 DLL 编译与同步。
        File.WriteAllText("Assets/Package_Code/package.json", "{\"schema\":1}");
        Directory.CreateDirectory("Assets/Package_Modules/SOConfig");
        const string qualityPath = "Assets/Package_Modules/SOConfig/QualitySOConfig.asset";
        if (!AssetDatabase.LoadAssetAtPath<QualitySOConfig>(qualityPath))
        {
            var quality = ScriptableObject.CreateInstance<QualitySOConfig>();
            foreach (GfxQualityTierType tier in Enum.GetValues(typeof(GfxQualityTierType)))
                quality.Tiers.Add(new GfxQualityTierEntry { Tier = tier, DisplayName = tier.ToString(), TargetFrameRate = 60 });
            AssetDatabase.CreateAsset(quality, qualityPath);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        setting.CheckAllPackageConfigError();
        Debug.Log("[LittleGameStackSetup] 7 个包与 4 个热更程序集配置完成");
    }

    static void SetPackage(AssetBundleCollectorSetting setting, string name, string root)
    {
        Directory.CreateDirectory(root);
        var package = setting.Packages.Find(p => p.PackageName == name);
        if (package == null)
        {
            package = new AssetBundleCollectorPackage { PackageName = name };
            setting.Packages.Add(package);
        }
        package.EnableAddressable = true;
        package.SupportExtensionless = true;
        var group = package.Groups.Find(g => g.GroupName == "LittleGame");
        if (group == null)
        {
            group = new AssetBundleCollectorGroup { GroupName = "LittleGame" };
            package.Groups.Add(group);
        }
        group.Collectors.Clear();
        group.Collectors.Add(new AssetBundleCollector
        {
            CollectPath = root,
            AddressRuleName = nameof(AddressByBundleAndRelativePath),
            PackRuleName = nameof(PackSeparately),
            FilterRuleName = nameof(CollectAll)
        });
    }
}
