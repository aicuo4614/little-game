using System.Collections.Generic;
using System.IO;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 编译热更 DLL 并自动同步到 Package_Code 资源目录。
///
/// 目标映射（均放在 CodePackage 里供 YooAsset 打包）：
///   HybridCLRData/HotUpdateDlls/{Target}/HotUpdate_UnitCore.dll
///     → Assets/Package_Code/HotUpdate_UnitCore/HotUpdate_UnitCore.dll.bytes
///   HybridCLRData/HotUpdateDlls/{Target}/HotUpdate_Main.dll
///     → Assets/Package_Code/HotUpdate_Main/HotUpdate_Main.dll.bytes
///
/// 如需增加 DLL，在 DllCopyConfigs 中追加映射即可。
/// </summary>
public static class HybridCLRBuilder
{
    // DLL 名称需与对应 .asmdef 的 name 字段大小写完全一致
    private static Dictionary<string, string> dllCopyConfigs = new Dictionary<string, string>()
    {
        ["HotUpdate_UnitCore.dll"] = "Assets/Package_Code/HotUpdate_UnitCore/HotUpdate_UnitCore.dll.bytes",
        ["LittleGame.Domain.dll"] = "Assets/Package_Code/HotUpdate_Domain/LittleGame.Domain.dll.bytes",
        ["LittleGame.Client.dll"] = "Assets/Package_Code/LittleGame_Client/LittleGame.Client.dll.bytes",
        ["HotUpdate_Main.dll"]     = "Assets/Package_Code/HotUpdate_Main/HotUpdate_Main.dll.bytes",
    };

    [MenuItem("HybridCLR/编译热更 DLL 并同步到 Package_Code", priority = 50)]
    public static void CompileAndSync()
    {
        var target = EditorUserBuildSettings.activeBuildTarget;

        // Step 1：Generate All — 分析所有热更程序集，更新 AOTGenericReferences.cs
        // 必须在 CompileDll 之前执行，确保 PatchedAOTAssemblyList 包含所有程序集的泛型需求
        Debug.Log($"[HybridCLRBuilder] Step 1/3 - Generate All，目标平台: {target}");
        PrebuildCommand.GenerateAll();

        // Step 2：编译所有热更 DLL
        Debug.Log($"[HybridCLRBuilder] Step 2/3 - 编译热更 DLL");
        CompileDllCommand.CompileDll(target);

        // Step 3：同步热更 DLL + AOT 元数据 manifest.json 到 Package_Code
        Debug.Log("[HybridCLRBuilder] Step 3/3 - 同步到 Package_Code");
        string dllOutputDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
        SyncDlls(dllOutputDir);
        AOTMetadataGenerator.SyncAndGenerate(refreshAssets: false);

        AssetDatabase.Refresh();
        Debug.Log("[HybridCLRBuilder] 完成！热更 DLL 及 AOT 元数据已同步到 Package_Code。");
    }

    /// <summary>
    /// 仅执行同步（不重新编译），用于手动补同步或 CI 场景。
    /// </summary>
    [MenuItem("HybridCLR/仅同步热更 DLL 到 Package_Code", priority = 51)]
    public static void SyncOnly()
    {
        var target = EditorUserBuildSettings.activeBuildTarget;
        string dllOutputDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
        SyncDlls(dllOutputDir);
        AssetDatabase.Refresh();
        Debug.Log("[HybridCLRBuilder] 同步完成。");
    }

    private static void SyncDlls(string dllOutputDir)
    {
        foreach (var (dllName, destRelPath) in dllCopyConfigs)
        {
            string srcPath  = Path.Combine(dllOutputDir, dllName);
            string destPath = Path.GetFullPath(destRelPath);

            if (!File.Exists(srcPath))
            {
                throw new FileNotFoundException("缺少热更 DLL，终止同步", srcPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            // 内容相同就保留原文件，避免无效覆盖触发 Unity 资源导入。
            var existingFiles = Directory.GetFiles(Path.GetDirectoryName(destPath), Path.GetFileName(destPath));
            var exactFileName = existingFiles.Length == 1
                && string.Equals(Path.GetFileName(existingFiles[0]), Path.GetFileName(destPath), System.StringComparison.Ordinal);
            if (exactFileName && FileContentEquals(srcPath, destPath))
            {
                continue;
            }

            // Windows 覆盖不会修正文件名大小写，变化时仍保留原有删除再复制行为。
            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            File.Copy(srcPath, destPath, overwrite: true);
            Debug.Log($"[HybridCLRBuilder] 已同步: {dllName} → {destRelPath}");
        }
    }

    /// <summary>比较文件内容，用于跳过没有变化的构建产物；目标缺失时返回 false。</summary>
    public static bool FileContentEquals(string sourcePath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            return false;
        }

        using (var source = File.OpenRead(sourcePath))
        using (var destination = File.OpenRead(destinationPath))
        {
            if (source.Length != destination.Length)
            {
                return false;
            }

            var sourceBuffer = new byte[65536];
            var destinationBuffer = new byte[65536];
            int count;
            while ((count = source.Read(sourceBuffer, 0, sourceBuffer.Length)) > 0)
            {
                var offset = 0;
                while (offset < count)
                {
                    var read = destination.Read(destinationBuffer, offset, count - offset);
                    if (read == 0)
                    {
                        return false;
                    }

                    offset += read;
                }

                for (var i = 0; i < count; i++)
                {
                    if (sourceBuffer[i] != destinationBuffer[i])
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

}
