using System;
using System.IO;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEngine;

/// <summary>真实调用迁入的构建管线，记录完成结果，供 MCP 等待而不重复触发。</summary>
public static class LittleGameStackVerification
{
    [MenuItem("Little Game/Framework/Compile Hot DLLs")]
    public static void CompileHotDlls()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play");
        var target = EditorUserBuildSettings.activeBuildTarget;
        CompileDllCommand.CompileDll(target);
        HybridCLRBuilder.SyncOnly();
        Directory.CreateDirectory("Reports/Framework");
        File.WriteAllText("Reports/Framework/hot-dlls.json", JsonUtility.ToJson(new CompileResult
        {
            target = target.ToString(),
            completed = true,
            aotMetadataGenerated = false,
            output = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target),
            note = "仅编译和同步 DLL；没有构建 APK，也不代表 HybridCLR 原生运行验收。完整发布需 HybridCLRBuilder.CompileAndSync 生成元数据。"
        }, true));
    }

    [MenuItem("Little Game/Framework/Build Resource Packages")]
    public static void BuildPackages()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play");
        try
        {
            Directory.CreateDirectory("Reports/Framework");
            bool ok = KabuYooAssetBatchBuilder.TryBuildAllPackages(out var package, out var error, out var completed);
            File.WriteAllText("Reports/Framework/resource-build.json", JsonUtility.ToJson(new ResourceResult
            {
                completed = ok,
                failedPackage = package,
                error = error,
                packages = completed.ToArray(),
                target = EditorUserBuildSettings.activeBuildTarget.ToString(),
                cdnRoot = BuildConf.CdnRootFolder
            }, true));
        }
        catch (Exception error)
        {
            File.WriteAllText("Reports/Framework/resource-build.json", JsonUtility.ToJson(new ResourceResult
            {
                completed = false, error = error.ToString(), target = EditorUserBuildSettings.activeBuildTarget.ToString()
            }, true));
            throw;
        }
    }

    [Serializable] class CompileResult
    {
        public bool completed, aotMetadataGenerated;
        public string target, output, note;
    }
    [Serializable] class ResourceResult
    {
        public bool completed;
        public string failedPackage, error, target, cdnRoot;
        public string[] packages;
    }
}
