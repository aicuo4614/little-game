using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 将 YooAsset 构建输出目录同步到本地 CDN 目录
///
/// 源目录结构（构建输出，含版本子目录）：
///   {BuildOutputRoot}/{Platform}/{PackageName}/{Version}/
///     PackageName.version
///     PackageName_VERSION.bytes
///     PackageName_VERSION.hash
///     *.bundle
///     buildlogtep.json（跳过）
///     *.report（跳过）
///     link.xml（跳过）
///
/// 目标目录结构（CDN 扁平目录，无版本子目录）：
///   {cdn_root_folder}/{Platform}/{PackageName}/
///     PackageName.version       ← 每次覆盖（指向最新版本）
///     PackageName_VERSION.bytes ← 每次覆盖（新清单）
///     PackageName_VERSION.hash  ← 每次覆盖
///     *.bundle                  ← 仅新增（同名即同内容，跳过已存在）
/// </summary>
public static class CdnSyncUtil
{
    private static string[] SkipExtensions = { ".json", ".report", ".xml", ".log" };

    /// <summary>
    /// 同步一个包的构建输出到 CDN。
    /// </summary>
    /// <param name="packageOutputDir">YooAsset 的版本输出目录（如 Bundles/StandaloneWindows64/CodePackage/2026-03-09-982）</param>
    /// <param name="cdnRootFolder">CDN 根目录（来自 conf.json 的 cdn_root_folder，默认工程同级 LocalCdn）</param>
    /// <param name="deleteVersionDirAfterSync">同步完成后是否删除版本目录（默认 false）。</param>
    /// <returns>CDN 包目录绝对路径（如 LocalCdn/Android/MainPackage）</returns>
    public static string Sync(string packageOutputDir, string cdnRootFolder, bool deleteVersionDirAfterSync = false)
    {
        packageOutputDir = Path.GetFullPath(packageOutputDir.TrimEnd('/', '\\'));
        if (!Directory.Exists(packageOutputDir))
        {
            throw new DirectoryNotFoundException($"[CdnSync] 构建输出目录不存在: {packageOutputDir}");
        }

        // 从路径末尾依次解析 version / packageName / platform
        // .../{Platform}/{PackageName}/{Version}
        string pkgDir = Path.GetDirectoryName(packageOutputDir);
        string packageName = Path.GetFileName(pkgDir);
        string platformDir = Path.GetDirectoryName(pkgDir);
        string platform = Path.GetFileName(platformDir);

        if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(platform))
        {
            throw new Exception($"[CdnSync] 无法从输出路径解析 Platform/PackageName: {packageOutputDir}");
        }

        if (!Directory.Exists(cdnRootFolder))
        {
            Directory.CreateDirectory(cdnRootFolder);
        }

        string destDir = Path.Combine(cdnRootFolder, platform, packageName);
        Directory.CreateDirectory(destDir);

        int copied = 0;
        int skipped = 0;

        var srcFiles = Directory.GetFiles(packageOutputDir, "*", SearchOption.TopDirectoryOnly);
        for (var i = 0; i < srcFiles.Length; i++)
        {
            string srcFile = srcFiles[i];
            string ext = Path.GetExtension(srcFile).ToLowerInvariant();
            string fileName = Path.GetFileName(srcFile);

            // 跳过构建日志、报告等客户端不需要的文件
            if (Array.Exists(SkipExtensions, e => e == ext))
            {
                continue;
            }

            string destFile = Path.Combine(destDir, fileName);

            // bundle 文件名含 hash，同名即同内容，已存在则跳过
            if (ext == ".bundle" && File.Exists(destFile))
            {
                skipped++;
                continue;
            }

            File.Copy(srcFile, destFile, overwrite: true);
            copied++;
        }

        Debug.Log($"[CdnSync] ✓ {packageName} → {destDir}  (copied={copied}, skipped={skipped})");

        if (deleteVersionDirAfterSync)
        {
            Directory.Delete(packageOutputDir, recursive: true);
            Debug.Log($"[CdnSync] 已删除版本目录: {packageOutputDir}");
        }

        return destDir;
    }

    /// <summary>在资源管理器中打开 CDN 包目录。</summary>
    public static void RevealInExplorer(string destDir)
    {
        if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
        {
            return;
        }

        EditorUtility.RevealInFinder(destDir);
    }
}
