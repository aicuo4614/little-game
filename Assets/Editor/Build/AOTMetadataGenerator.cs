using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 读取 HybridCLR 生成源码中的 PatchedAOTAssemblyList，自动完成：
///   1. 将裁剪版 AOT dll 拷贝到 Package_Code 对应目录（作为 .bytes 资源）
///   2. 生成 manifest.json，运行时由 HotUpdateMgr 动态读取，无需修改任何 AOT 代码
///
/// manifest.json 的 YooAsset 地址固定为 "HotUpdate_Main/AOTMetadata/manifest"。
/// 触发方式：由 HybridCLRBuilder.CompileAndSync() 自动调用，也可手动执行菜单。
/// </summary>
public static class AOTMetadataGenerator
{
    private const string AOTMetadataDestDir   = "Assets/Package_Code/HotUpdate_Main/AOTMetadata";
    private const string AOTMetadataAssetPath = "HotUpdate_Main/AOTMetadata";

    [MenuItem("HybridCLR/同步 AOT 元数据到 Package_Code", priority = 52)]
    public static void SyncAndGenerate()
    {
        SyncAndGenerate(refreshAssets: true);
    }

    /// <summary>同步 AOT 元数据；流水线调用时延后刷新，由外层统一导入。</summary>
    public static void SyncAndGenerate(bool refreshAssets)
    {
        var target = EditorUserBuildSettings.activeBuildTarget;

        string stripDir = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..",
            "HybridCLRData", "AssembliesPostIl2CppStrip",
            target.ToString()));

        if (!Directory.Exists(stripDir))
        {
            Debug.LogWarning($"[AOTMetadataGenerator] 裁剪 DLL 目录不存在，请先执行 HybridCLR Generate All：{stripDir}");
            return;
        }

        string referenceFile = Path.Combine(Application.dataPath, SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile);
        if (!File.Exists(referenceFile))
        {
            Debug.LogWarning($"[AOTMetadataGenerator] 泛型引用文件不存在，请先执行 HybridCLR Generate All：{referenceFile}");
            return;
        }

        // Generate All 可能刚写入源码，当前编辑器域还未加载新编译的程序集。
        var assemblyList = Regex.Match(File.ReadAllText(referenceFile),
            @"PatchedAOTAssemblyList\s*=\s*new\s+List<string>\s*\{([^}]*)\}\s*;");
        if (!assemblyList.Success)
        {
            Debug.LogError($"[AOTMetadataGenerator] 无法读取 PatchedAOTAssemblyList：{referenceFile}");
            return;
        }

        string destDir = Path.GetFullPath(AOTMetadataDestDir);
        Directory.CreateDirectory(destDir);
        var successPaths = new List<string>();
        foreach (Match entry in Regex.Matches(assemblyList.Groups[1].Value, "\"([^\"\\r\\n]+\\.dll)\""))
        {
            string dll = entry.Groups[1].Value;
            string srcPath = Path.Combine(stripDir, dll);
            string destPath = Path.Combine(destDir, dll + ".bytes");
            if (!File.Exists(srcPath))
            {
                Debug.LogWarning($"[AOTMetadataGenerator] 源文件不存在，跳过：{srcPath}");
                continue;
            }

            if (!HybridCLRBuilder.FileContentEquals(srcPath, destPath))
            {
                File.Copy(srcPath, destPath, overwrite: true);
            }
            successPaths.Add($"{AOTMetadataAssetPath}/{dll}");
            Debug.Log($"[AOTMetadataGenerator] 已拷贝：{dll} → {AOTMetadataDestDir}/{dll}.bytes");
        }

        GenerateManifest(destDir, successPaths);
        if (refreshAssets)
        {
            AssetDatabase.Refresh();
        }
        Debug.Log($"[AOTMetadataGenerator] 完成！共同步 {successPaths.Count} 个 AOT 元数据文件，manifest.json 已更新。");
    }

    // manifest.json 内容示例：{"paths":["HotUpdate_Main/AOTMetadata/mscorlib.dll",...]}
    // 由 HotUpdateMgr 在运行时加载后解析，AOT 代码无需感知具体路径列表。
    private static void GenerateManifest(string destDir, IReadOnlyList<string> paths)
    {
        var sb = new StringBuilder();
        sb.Append("{\"paths\":[");
        for (int i = 0; i < paths.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append('"').Append(paths[i]).Append('"');
        }
        sb.Append("]}");

        string manifestPath = Path.Combine(destDir, "manifest.json");
        var content = sb.ToString();
        if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath, Encoding.UTF8) != content)
        {
            File.WriteAllText(manifestPath, content, Encoding.UTF8);
        }
        Debug.Log($"[AOTMetadataGenerator] manifest.json 已写入：{manifestPath}");
    }
}
