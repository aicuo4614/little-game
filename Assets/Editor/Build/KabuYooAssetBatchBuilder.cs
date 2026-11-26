using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

/// <summary>
/// 构建单工程七个 YooAsset 包，并同步到 conf.json 指定的本地 CDN。
/// 全部包内置首包，同时同步到本地 CDN 以验证 Host 更新。
/// 场景、单位与特效在同一工程构建。
/// </summary>
public static class KabuYooAssetBatchBuilder
{
    private struct PackageBuildPlan
    {
        public string PackageName;
        public EBuildinFileCopyOption BuildinCopyOption;
    }

    private static readonly PackageBuildPlan[] DefaultPlans =
    {
        new PackageBuildPlan { PackageName = "LaunchPackage", BuildinCopyOption = EBuildinFileCopyOption.ClearAndCopyAll },
        new PackageBuildPlan { PackageName = "CodePackage", BuildinCopyOption = EBuildinFileCopyOption.ClearAndCopyAll },
        new PackageBuildPlan { PackageName = "MainPackage", BuildinCopyOption = EBuildinFileCopyOption.ClearAndCopyAll },
        new PackageBuildPlan { PackageName = "ModulePackage", BuildinCopyOption = EBuildinFileCopyOption.ClearAndCopyAll },
        new PackageBuildPlan { PackageName = "UnitPackage", BuildinCopyOption = EBuildinFileCopyOption.ClearAndCopyAll },
        new PackageBuildPlan { PackageName = "EffectPackage", BuildinCopyOption = EBuildinFileCopyOption.ClearAndCopyAll },
        new PackageBuildPlan { PackageName = "ScenePackage", BuildinCopyOption = EBuildinFileCopyOption.ClearAndCopyAll },
    };

    /// <summary>
    /// 由 KabuScriptableBuildPipeline 面板「Click Build」下方的按钮调用。
    /// </summary>
    public static void BuildAllPackages()
    {
        var target = EditorUserBuildSettings.activeBuildTarget;
        if (target != BuildTarget.StandaloneWindows64)
        {
            if (!EditorUtility.DisplayDialog(
                    "平台提示",
                    $"当前 Build Target 是 {target}，不是 StandaloneWindows64。\n仍要继续按当前平台打包吗？",
                    "继续",
                    "取消"))
            {
                return;
            }
        }

        string cdnRoot;
        try
        {
            cdnRoot = BuildConf.CdnRootFolder;
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("无法读取 CDN 配置", e.Message + "\n\n请先创建工程根目录 conf.json。", "确定");
            return;
        }

        var summary = new StringBuilder();
        summary.AppendLine("将依次打包并同步 CDN：");
        for (var i = 0; i < DefaultPlans.Length; i++)
        {
            var plan = DefaultPlans[i];
            summary.AppendLine($"  {i + 1}. {plan.PackageName}  (BuildinCopy={plan.BuildinCopyOption})");
        }

        summary.AppendLine();
        summary.AppendLine($"平台：{target}");
        summary.AppendLine($"CDN：{cdnRoot}");

        if (!EditorUtility.DisplayDialog("一键打包", summary.ToString(), "开始", "取消"))
        {
            return;
        }

        if (!TryBuildAllPackages(out var failPackage, out var failError, out var okList, showProgress: true))
        {
            EditorUtility.DisplayDialog(
                "打包中断",
                $"成功：{string.Join(", ", okList)}\n失败：{failPackage}\n原因：{failError}",
                "确定");
            Debug.LogError($"[KabuBatchBuild] 中断于 {failPackage}: {failError}");
            return;
        }

        EditorUtility.DisplayDialog(
            "打包完成",
            $"已打包并同步 CDN：\n{string.Join("\n", okList)}\n\nCDN：{cdnRoot}",
            "确定");
        Debug.Log($"[KabuBatchBuild] 全部完成 → {cdnRoot}");
    }

    /// <summary>
    /// 无确认弹窗打包全部单工程包（供一键 Android 流水线调用）。
    /// </summary>
    /// <returns>全部成功返回 true。</returns>
    public static bool TryBuildAllPackages(
        out string failPackage,
        out string failError,
        out List<string> okList,
        bool showProgress = false)
    {
        failPackage = string.Empty;
        failError = string.Empty;
        okList = new List<string>();

        var target = EditorUserBuildSettings.activeBuildTarget;
        string cdnRoot;
        try
        {
            cdnRoot = BuildConf.CdnRootFolder;
        }
        catch (Exception e)
        {
            failPackage = "(init)";
            failError = e.Message;
            return false;
        }

        try
        {
            for (var i = 0; i < DefaultPlans.Length; i++)
            {
                var plan = DefaultPlans[i];
                if (showProgress)
                {
                    var progress = (float)i / DefaultPlans.Length;
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "YooAsset 一键打包",
                            $"[{i + 1}/{DefaultPlans.Length}] {plan.PackageName}",
                            progress))
                    {
                        failPackage = plan.PackageName;
                        failError = "用户取消";
                        return false;
                    }
                }

                if (!BuildOnePackage(plan, target, cdnRoot, out var error))
                {
                    failPackage = plan.PackageName;
                    failError = error;
                    return false;
                }

                okList.Add(plan.PackageName);
            }
        }
        finally
        {
            if (showProgress)
            {
                EditorUtility.ClearProgressBar();
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[KabuBatchBuild] 全部完成 → {cdnRoot}");
        return true;
    }

    private const string PipelineName = "ScriptableBuildPipeline";

    /// <summary>只构建包含战斗角色映射的模块资源包并同步本地 CDN。</summary>
    [MenuItem("Kabu/打包 ModulePackage", priority = 52)]
    public static void BuildModulePackage()
    {
        var plan = new PackageBuildPlan
        {
            PackageName = "ModulePackage",
            BuildinCopyOption = EBuildinFileCopyOption.ClearAndCopyAll,
        };
        if (!BuildOnePackage(plan, EditorUserBuildSettings.activeBuildTarget, BuildConf.CdnRootFolder, out var error))
        {
            Debug.LogError($"[KabuBatchBuild] ModulePackage failed: {error}");
            return;
        }
        AssetDatabase.Refresh();
        Debug.Log("[KabuBatchBuild] ModulePackage synced to local CDN");
    }

    private static bool BuildOnePackage(PackageBuildPlan plan, BuildTarget target, string cdnRoot, out string error)
    {
        error = null;
        string version = GetDefaultPackageVersion();

        Debug.Log($"[KabuBatchBuild] 开始 {plan.PackageName} version={version} copy={plan.BuildinCopyOption}");

        // FileNameStyle 不用 EditorPrefs：Yoo 默认 HashName，新包未开过 Builder 会踩坑。
        // 项目约定一律 BundleName_HashName；压缩/清缓存等仍读面板。BuildinCopy 仍用计划表。
        var fileNameStyle = EFileNameStyle.BundleName_HashName;
        AssetBundleBuilderSetting.SetPackageFileNameStyle(plan.PackageName, PipelineName, fileNameStyle);
        var compressOption = AssetBundleBuilderSetting.GetPackageCompressOption(plan.PackageName, PipelineName);
        var clearBuildCache = AssetBundleBuilderSetting.GetPackageClearBuildCache(plan.PackageName, PipelineName);
        var useAssetDependencyDB = AssetBundleBuilderSetting.GetPackageUseAssetDependencyDB(plan.PackageName, PipelineName);

        var buildParameters = new ScriptableBuildParameters
        {
            BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
            BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
            BuildPipeline = PipelineName,
            BuildBundleType = (int)EBuildBundleType.AssetBundle,
            BuildTarget = target,
            PackageName = plan.PackageName,
            PackageVersion = version,
            EnableSharePackRule = true,
            // 单引用依赖并进引用它的主包；多引用仍打 share
            SingleReferencedPackAlone = false,
            VerifyBuildingResult = true,
            FileNameStyle = fileNameStyle,
            BuildinFileCopyOption = plan.BuildinCopyOption,
            BuildinFileCopyParams = string.Empty,
            CompressOption = compressOption,
            ClearBuildCacheFiles = clearBuildCache,
            UseAssetDependencyDB = useAssetDependencyDB,
            EncryptionServices = null,
            ManifestProcessServices = null,
            ManifestRestoreServices = null,
            BuiltinShadersBundleName = GetBuiltinShaderBundleName(plan.PackageName),
        };

        var pipeline = new ScriptableBuildPipeline();
        var buildResult = pipeline.Run(buildParameters, true);
        if (!buildResult.Success)
        {
            error = string.IsNullOrEmpty(buildResult.ErrorInfo)
                ? $"FailedTask={buildResult.FailedTask}"
                : buildResult.ErrorInfo;
            return false;
        }

        try
        {
            var destDir = CdnSyncUtil.Sync(buildResult.OutputPackageDirectory, cdnRoot);
            Debug.Log($"[KabuBatchBuild] ✓ {plan.PackageName} FileNameStyle={fileNameStyle} → {destDir}");
        }
        catch (Exception e)
        {
            error = $"CDN 同步失败: {e.Message}";
            return false;
        }

        return true;
    }

    private static string GetDefaultPackageVersion()
    {
        int totalMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
        return DateTime.Now.ToString("yyyy-MM-dd") + "-" + totalMinutes;
    }

    private static string GetBuiltinShaderBundleName(string packageName)
    {
        var uniqueBundleName = AssetBundleCollectorSettingData.Setting.UniqueBundleName;
        var packRuleResult = DefaultPackRule.CreateShadersPackRuleResult();
        return packRuleResult.GetBundleName(packageName, uniqueBundleName);
    }
}
