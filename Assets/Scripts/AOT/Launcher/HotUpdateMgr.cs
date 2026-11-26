using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using YooAsset;

/// <summary>
/// 热更新流程管理器
///
/// 两阶段设计：
///   Phase 1 - 全部包版本检查（统计总下载量，保证聚合进度准确）
///   Phase 2 - 统一顺序下载（提供跨包聚合进度）
///   Phase 3 - 加载 DLL 程序集
///
/// 包名使用 string 而非枚举，支持运营阶段动态新增分包。
/// 事件通过 FlowHandle 隔离，每次 RunFlow 独立，无需手动清理。
/// </summary>
public class HotUpdateMgr
{
    private static HotUpdateMgr _instance;
    public static HotUpdateMgr Instance => _instance ??= new HotUpdateMgr();

    // ===================== 包流程配置 =====================

    public struct PackageFlowConfig
    {
        /// <summary>包名（string，支持动态新增包，不受枚举限制）</summary>
        public string PackageName;

        /// <summary>DLL 资源路径（.bytes），null 表示纯资源包无需加载 DLL</summary>
        public string[] DllAssetPaths;
        
        /// <summary>
        /// AOT 元数据清单文件的 YooAsset 地址（manifest.json），与热更包一同下发。
        /// 运行时先加载此清单，再按其中列表注入 AOT 补充元数据，使 IL2CPP 泛型正常工作。
        /// 固定地址：HotUpdate_Main/AOTMetadata/manifest
        /// 由 AOTMetadataGenerator 在编译热更 DLL 时自动生成，无需手动维护。
        /// </summary>
        public string AotManifestPath;

        /// <summary>
        /// Tag 过滤下载：null = 下载整包；非 null = 只下载含指定 Tag 的资源
        /// </summary>
        public string[] DownloadTags;

        /// <summary>枚举便捷构造</summary>
        public PackageFlowConfig(ResourcePackageName name, string[] dllPaths = null,
            string aotManifestPath = null, string[] tags = null)
        {
            PackageName    = name.ToPackageString();
            DllAssetPaths  = dllPaths;
            AotManifestPath = aotManifestPath;
            DownloadTags   = tags;
        }
    }

    // ===================== FlowHandle（每次 RunFlow 独立） =====================

    public class FlowHandle
    {
        /// <summary>某个包开始版本检查。参数：包名</summary>
        public event Action<string> OnPackageCheckStart;

        /// <summary>某个包版本检查完成 (pkgName, version, needDownload, files, bytes)</summary>
        public event Action<string, string, bool, int, long> OnPackageCheckComplete;

        /// <summary>某个包版本检查失败 (pkgName, error)</summary>
        public event Action<string, string> OnPackageCheckFailed;

        /// <summary>所有包检查完毕，总下载量已知 (totalFiles, totalBytes)</summary>
        public event Action<int, long> OnTotalSizeKnown;

        /// <summary>某个包开始下载 (pkgName, files, bytes)</summary>
        public event Action<string, int, long> OnPackageDownloadBegin;

        /// <summary>
        /// 下载进度更新
        /// pkgName | pkgData: 当前包进度 | aggregatedProgress: 0~1 | aggregatedBytes | totalBytes
        /// </summary>
        public event Action<string, DownloadUpdateData, float, long, long> OnPackageDownloadProgress;

        /// <summary>单文件下载失败 (pkgName, fileName, error)</summary>
        public event Action<string, string, string> OnPackageDownloadError;

        /// <summary>某个包下载完成</summary>
        public event Action<string> OnPackageDownloadComplete;

        /// <summary>某个包下载失败 (pkgName, error)</summary>
        public event Action<string, string> OnPackageDownloadFailed;

        /// <summary>某个包开始加载 DLL</summary>
        public event Action<string> OnPackageAssemblyLoadStart;

        /// <summary>某个包 DLL 加载完成</summary>
        public event Action<string, IReadOnlyList<Assembly>> OnPackageAssemblyLoadComplete;

        /// <summary>全部流程结束 (success)</summary>
        public event Action<bool> OnAllFlowComplete;

        internal void InvokeCheckStart(string p)                                                        => OnPackageCheckStart?.Invoke(p);
        internal void InvokeCheckComplete(string p, string v, bool n, int c, long b)                   => OnPackageCheckComplete?.Invoke(p, v, n, c, b);
        internal void InvokeCheckFailed(string p, string e)                                            => OnPackageCheckFailed?.Invoke(p, e);
        internal void InvokeTotalSizeKnown(int f, long b)                                              => OnTotalSizeKnown?.Invoke(f, b);
        internal void InvokeDownloadBegin(string p, int c, long b)                                     => OnPackageDownloadBegin?.Invoke(p, c, b);
        internal void InvokeDownloadProgress(string p, DownloadUpdateData d, float ap, long ab, long tb) => OnPackageDownloadProgress?.Invoke(p, d, ap, ab, tb);
        internal void InvokeDownloadError(string p, string f, string e)                                => OnPackageDownloadError?.Invoke(p, f, e);
        internal void InvokeDownloadComplete(string p)                                                  => OnPackageDownloadComplete?.Invoke(p);
        internal void InvokeDownloadFailed(string p, string e)                                         => OnPackageDownloadFailed?.Invoke(p, e);
        internal void InvokeAssemblyLoadStart(string p)                                                => OnPackageAssemblyLoadStart?.Invoke(p);
        internal void InvokeAssemblyLoadComplete(string p, IReadOnlyList<Assembly> a)                  => OnPackageAssemblyLoadComplete?.Invoke(p, a);
        internal void InvokeAllFlowComplete(bool s)                                                    => OnAllFlowComplete?.Invoke(s);
    }

    // ===================== 运行时状态 =====================

    /// <summary>本次 RunFlow 加载的热更程序集</summary>
    public IReadOnlyList<Assembly> AllAssemblies { get; private set; }

    private readonly List<Assembly> _allAssemblies = new();

    // ===================== 主流程入口 =====================

    public IEnumerator RunFlow(PackageFlowConfig[] configs, FlowHandle handle)
    {
        // ---- Phase 1: 全部包版本检查 ----
        var downloaders = new ResourceDownloaderOperation[configs.Length];
        long totalBytes = 0;
        int  totalFiles = 0;

        for (int i = 0; i < configs.Length; i++)
        {
            bool ok = false;
            ResourceDownloaderOperation dl = null;
            yield return CheckAndUpdateVersion(configs[i], handle, (success, downloader) =>
            {
                ok = success;
                dl = downloader;
            });

            if (!ok)
            {
                AllAssemblies = _allAssemblies.AsReadOnly();
                handle.InvokeAllFlowComplete(false);
                yield break;
            }

            downloaders[i] = dl;
            if (dl != null)
            {
                totalBytes += dl.TotalDownloadBytes;
                totalFiles += dl.TotalDownloadCount;
            }
        }

        handle.InvokeTotalSizeKnown(totalFiles, totalBytes);

        // ---- Phase 2: 统一下载 ----
        long cumulativeBytes = 0;

        for (int i = 0; i < configs.Length; i++)
        {
            bool ok = false;
            long pkgBytes = 0;
            yield return DownloadIfNeeded(configs[i].PackageName, downloaders[i],
                cumulativeBytes, totalBytes, handle,
                (success, completedBytes) => { ok = success; pkgBytes = completedBytes; });

            if (!ok)
            {
                AllAssemblies = _allAssemblies.AsReadOnly();
                handle.InvokeAllFlowComplete(false);
                yield break;
            }

            cumulativeBytes += pkgBytes;
        }

        // ---- Phase 3: 加载 DLL ----
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].DllAssetPaths == null || configs[i].DllAssetPaths.Length == 0)
                continue;

            bool ok = false;
            yield return LoadAssemblies(configs[i].PackageName, configs[i].DllAssetPaths,
                configs[i].AotManifestPath, handle,
                (success, assemblies) =>
                {
                    ok = success;
                    if (success && assemblies != null) _allAssemblies.AddRange(assemblies);
                });

            if (!ok)
            {
                AllAssemblies = _allAssemblies.AsReadOnly();
                handle.InvokeAllFlowComplete(false);
                yield break;
            }
        }

        AllAssemblies = _allAssemblies.AsReadOnly();
        handle.InvokeAllFlowComplete(true);
    }

    // ===================== Phase 1: 版本检查 =====================

    private IEnumerator CheckAndUpdateVersion(
        PackageFlowConfig config,
        FlowHandle handle,
        Action<bool, ResourceDownloaderOperation> result)
    {
        string packageName = config.PackageName;
        var package = YooAssets.GetPackage(packageName);

        if (package == null)
        {
            string err = $"Package not found: {packageName}";
            GameDebug.LogError("HotUpdate", $"{err}");
            handle.InvokeCheckFailed(packageName, err);
            result(false, null);
            yield break;
        }

        handle.InvokeCheckStart(packageName);

        // EditorSimulateMode 同样需要 RequestPackageVersion + UpdatePackageManifest 才能激活 Manifest，
        // 此后 CreateResourceDownloader 才不会抛 "Can not found active package manifest"。
        var versionOp = package.RequestPackageVersionAsync();
        yield return versionOp;

        if (versionOp.Status != EOperationStatus.Succeed)
        {
#if UNITY_EDITOR
            GameDebug.LogError("HotUpdate", $"{packageName} 版本获取失败: {versionOp.Error}");
            handle.InvokeCheckFailed(packageName, versionOp.Error);
            result(false, null);
#else
            if (package.PackageValid)
            {
                GameDebug.LogWarning("HotUpdate", $"{packageName} 网络不可达，使用本地缓存版本");
                handle.InvokeCheckComplete(packageName, "cached", false, 0, 0);
                result(true, CreateDownloader(package, config));
            }
            else
            {
                GameDebug.LogError("HotUpdate", $"{packageName} 版本获取失败且无本地缓存: {versionOp.Error}");
                handle.InvokeCheckFailed(packageName, versionOp.Error);
                result(false, null);
            }
#endif
            yield break;
        }

        string remoteVersion = versionOp.PackageVersion;

        var manifestOp = package.UpdatePackageManifestAsync(remoteVersion);
        yield return manifestOp;

        if (manifestOp.Status != EOperationStatus.Succeed)
        {
#if UNITY_EDITOR
            GameDebug.LogError("HotUpdate", $"{packageName} 清单更新失败: {manifestOp.Error}");
            handle.InvokeCheckFailed(packageName, manifestOp.Error);
            result(false, null);
#else
            if (package.PackageValid)
            {
                GameDebug.LogWarning("HotUpdate", $"{packageName} 清单更新失败，使用本地缓存版本");
                handle.InvokeCheckComplete(packageName, remoteVersion, false, 0, 0);
                result(true, CreateDownloader(package, config));
            }
            else
            {
                GameDebug.LogError("HotUpdate", $"{packageName} 清单更新失败且无本地缓存: {manifestOp.Error}");
                handle.InvokeCheckFailed(packageName, manifestOp.Error);
                result(false, null);
            }
#endif
            yield break;
        }

        var dl = CreateDownloader(package, config);
        handle.InvokeCheckComplete(packageName, remoteVersion,
            dl.TotalDownloadCount > 0,
            dl.TotalDownloadCount,
            dl.TotalDownloadBytes);
        result(true, dl);
    }

    private static ResourceDownloaderOperation CreateDownloader(ResourcePackage package, PackageFlowConfig config)
    {
        if (config.DownloadTags != null && config.DownloadTags.Length > 0)
        {
            return package.CreateResourceDownloader(config.DownloadTags, 10, 3);
        }

        return package.CreateResourceDownloader(10, 3);
    }

    // ===================== Phase 2: 下载 =====================

    private IEnumerator DownloadIfNeeded(
        string packageName,
        ResourceDownloaderOperation downloader,
        long cumulativeBytes,
        long totalBytes,
        FlowHandle handle,
        Action<bool, long> result)
    {
        if (downloader == null || downloader.TotalDownloadCount == 0)
        {
            result(true, 0);
            yield break;
        }

        handle.InvokeDownloadBegin(packageName, downloader.TotalDownloadCount, downloader.TotalDownloadBytes);

        downloader.DownloadUpdateCallback = data =>
        {
            long   aggregatedBytes    = cumulativeBytes + data.CurrentDownloadBytes;
            float  aggregatedProgress = totalBytes > 0 ? (float)aggregatedBytes / totalBytes : data.Progress;
            handle.InvokeDownloadProgress(packageName, data, aggregatedProgress, aggregatedBytes, totalBytes);
        };

        downloader.DownloadErrorCallback = data =>
            handle.InvokeDownloadError(packageName, data.FileName, data.ErrorInfo);

        downloader.BeginDownload();
        yield return downloader;

        if (downloader.Status == EOperationStatus.Succeed)
        {
            handle.InvokeDownloadComplete(packageName);
            result(true, downloader.TotalDownloadBytes);
        }
        else
        {
            GameDebug.LogError("HotUpdate", $"{packageName} 下载失败: {downloader.Error}");
            handle.InvokeDownloadFailed(packageName, downloader.Error);
            result(false, 0);
        }
    }

    // ===================== Phase 3: 加载程序集 =====================

    // manifest.json 的反序列化结构（与 AOTMetadataGenerator 生成格式对应）
    [Serializable]
    private class AotManifest
    {
        public string[] paths;
    }

    private IEnumerator LoadAssemblies(
        string packageName,
        string[] dllPaths,
        string aotManifestPath,
        FlowHandle handle,
        Action<bool, List<Assembly>> result)
    {
        handle.InvokeAssemblyLoadStart(packageName);

#if UNITY_EDITOR
        // Editor 统一走 asmdef 直编程序集，避免与 CDN Assembly.Load 双程序集导致 SO/Prefab 脚本类型不一致。
        var empty = new List<Assembly>();
        handle.InvokeAssemblyLoadComplete(packageName, empty.AsReadOnly());
        result(true, empty);
        yield break;
#else
        var package = YooAssets.GetPackage(packageName);
        var assemblies = new List<Assembly>();

        // 先从 CodePackage 加载 manifest.json，动态获取 AOT 元数据路径列表
        // manifest 与热更包一同下发，路径列表随热更代码自动更新，无需修改 AOT 层代码
        if (!string.IsNullOrEmpty(aotManifestPath))
        {
            string[] aotMetadataPaths = null;
            var mh = package.LoadAssetAsync<UnityEngine.TextAsset>(aotManifestPath);
            yield return mh;

            if (mh.Status == EOperationStatus.Succeed)
            {
                var manifest = JsonUtility.FromJson<AotManifest>(((UnityEngine.TextAsset)mh.AssetObject).text);
                aotMetadataPaths = manifest?.paths;
                mh.Release();
            }
            else
            {
                GameDebug.LogWarning("HotUpdate", $"AOT元数据清单加载失败(忽略): {aotManifestPath} → {mh.LastError}");
                mh.Release();
            }

            if (aotMetadataPaths != null)
            {
                foreach (var metaPath in aotMetadataPaths)
                {
                    var h = package.LoadAssetAsync<UnityEngine.TextAsset>(metaPath);
                    yield return h;

                    if (h.Status != EOperationStatus.Succeed)
                    {
                        GameDebug.LogWarning("HotUpdate", $"AOT元数据加载失败(忽略): {metaPath} → {h.LastError}");
                        h.Release();
                        continue;
                    }

                    byte[] metaBytes = ((UnityEngine.TextAsset)h.AssetObject).bytes;
                    h.Release();

                    var errCode = HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(metaBytes, HybridCLR.HomologousImageMode.SuperSet);
                    if (errCode != HybridCLR.LoadImageErrorCode.OK)
                        GameDebug.LogWarning("HotUpdate", $"AOT元数据 {metaPath} 加载结果: {errCode}");
                    else
                        GameDebug.Log("HotUpdate", $"AOT元数据已注入: {metaPath}");
                }
            }
        }

        // 再加载热更 DLL（.dll.bytes 打在 AssetBundle 里，作为 TextAsset 加载）
        foreach (var dllPath in dllPaths)
        {
            var h = package.LoadAssetAsync<UnityEngine.TextAsset>(dllPath);
            yield return h;

            if (h.Status != EOperationStatus.Succeed)
            {
                GameDebug.LogError("HotUpdate", $"DLL 加载失败: {dllPath} → {h.LastError}");
                result(false, null);
                yield break;
            }

            byte[] bytes = ((UnityEngine.TextAsset)h.AssetObject).bytes;
            h.Release();

            GameDebug.Log("HotUpdate", $"DLL 字节长度: {bytes.Length}, 头部: {(bytes.Length >= 2 ? $"0x{bytes[0]:X2}{bytes[1]:X2}" : "N/A")}");

            Assembly assembly;
            try
            {
                assembly = Assembly.Load(bytes);
            }
            catch (System.BadImageFormatException ex)
            {
                GameDebug.LogError("HotUpdate", $"DLL 格式非法（请确保使用 HybridCLR/CompileDll 编译后同步）: {dllPath}\n{ex.Message}");
                result(false, null);
                yield break;
            }

            assemblies.Add(assembly);
            GameDebug.Log("HotUpdate", $"程序集已加载: {assembly.FullName}");
        }

        handle.InvokeAssemblyLoadComplete(packageName, assemblies.AsReadOnly());
        result(true, assemblies);
#endif
    }
}
