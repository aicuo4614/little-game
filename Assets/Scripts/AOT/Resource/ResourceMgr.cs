using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using YooAsset;

/// <summary>
/// AOT 编译期已知的资源包名称（与 Demo 对齐，单工程合并美术与程序资源包）。
/// </summary>
public enum ResourcePackageName
{
    LaunchPackage,
    CodePackage,
    MainPackage,
    ModulePackage,
    ResPackage,
    ActivityPackage,
    UnitPackage,
    EffectPackage,
    ScenePackage,
    IconPackage,
    StoryPackage,
}

public static class ResourcePackageNameExtensions
{
    public static string ToPackageString(this ResourcePackageName packageName)
        => packageName.ToString();
}

/// <summary>
/// 单工程 YooAsset 包初始化与生命周期管理器（对齐 Demo ResourceMgr）。
/// Editor 默认全部包走 EditorSimulateMode，勾选 LaunchSOConfig.EditorSimulateDevice 或真机走 Offline + HostPlayMode。
/// </summary>
public class ResourceMgr
{
    private const string DefaultCdnPath = "LaunchConfig/cdn.default";
    private const string OverrideCdnPath = "LaunchConfig/cdn";

    private static ResourceMgr _instance;
    public static ResourceMgr Instance => _instance ??= new ResourceMgr();

    private readonly Dictionary<string, ResourcePackage> _packages = new Dictionary<string, ResourcePackage>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
    }

    public string CdnBase { get; private set; } = "http://localhost:" + LaunchConfigKey.CdnPort;

    public void ApplyLaunchCdn()
    {
        var overrideAsset = Resources.Load<TextAsset>(OverrideCdnPath);
        if (overrideAsset != null)
        {
            var data = JsonUtility.FromJson<LaunchCdnOverrideFile>(overrideAsset.text);
            if (data != null && data.TryGetSelectedUrl(out var overrideUrl))
            {
                SetCdnBase(overrideUrl);
                GameDebug.Log("ResourceMgr", $"CDN 使用覆盖配置 [{data.index}] {CdnBase}");
                return;
            }
        }

        var defaultAsset = Resources.Load<TextAsset>(DefaultCdnPath);
        if (defaultAsset != null)
        {
            var data = JsonUtility.FromJson<LaunchCdnDefaultFile>(defaultAsset.text);
            if (data != null && data.TryGetUrl(out var defaultUrl))
            {
                SetCdnBase(defaultUrl);
                GameDebug.Log("ResourceMgr", $"CDN 使用 default {CdnBase}");
                return;
            }
        }
    }

    public void SetCdnBase(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        CdnBase = url.TrimEnd('/');
        GameDebug.Log("ResourceMgr", $"CdnBase = {CdnBase}");
    }

    private static string PlatformDir =>
#if UNITY_STANDALONE_OSX
        "StandaloneOSX";
#elif UNITY_ANDROID
        "Android";
#elif UNITY_IOS
        "iOS";
#else
        "StandaloneWindows64";
#endif

    public ResourcePackage GetPackage(string packageName)
        => _packages.TryGetValue(packageName, out var pkg) ? pkg : YooAssets.TryGetPackage(packageName);

    public ResourcePackage GetPackage(ResourcePackageName name)
        => GetPackage(name.ToPackageString());

    public void Init(Action<bool> callback)
    {
        if (!YooAssets.Initialized)
        {
            YooAssets.Initialize();
        }

        string pkgName = ResourcePackageName.LaunchPackage.ToPackageString();
        if (TryReuseHostPackage(pkgName, callback))
        {
            return;
        }

        InitOfflinePackage(pkgName, (success, package) =>
        {
            if (success && package != null)
            {
                _packages[pkgName] = package;
            }

            callback?.Invoke(success);
        });
    }

    public void InitHostPackage(string packageName, Action<bool> callback, bool cdnOnly = false)
    {
        if (!YooAssets.Initialized)
        {
            YooAssets.Initialize();
        }

        if (TryReuseHostPackage(packageName, callback))
        {
            return;
        }

        var package = YooAssets.CreatePackage(packageName);
        InitializeParameters initParams;

#if UNITY_EDITOR
        if (LaunchRuntimeResourceUtil.UseRuntimeResourcePipeline)
        {
            initParams = CreateHostPlayModeParameters(packageName, cdnOnly);
            GameDebug.Log("ResourceMgr", $"EditorSimulateDevice: {packageName} HostPlayMode cdnOnly={cdnOnly} → {CdnBase}");
        }
        else
        {
            // 单工程：Editor 默认全部包走 EditorSimulateMode，无需开启本地 CDN。
            try
            {
                var buildResult = EditorSimulateModeHelper.SimulateBuild(packageName);
                initParams = new EditorSimulateModeParameters
                {
                    EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory)
                };
            }
            catch (Exception ex)
            {
                GameDebug.LogWarning("ResourceMgr", $"{packageName} 未配置 YooAsset 收集器，跳过模拟初始化：{ex.Message}");
                YooAssets.RemovePackage(packageName);
                callback?.Invoke(false);
                return;
            }
        }
#else
        initParams = CreateHostPlayModeParameters(packageName, cdnOnly);
#endif

        package.InitializeAsync(initParams).Completed += handle =>
        {
            if (handle.Status != EOperationStatus.Succeed)
            {
                GameDebug.LogWarning("ResourceMgr", $"{packageName} 初始化失败: {handle.Error}");
                callback?.Invoke(false);
                return;
            }

            _packages[packageName] = package;
            GameDebug.Log("ResourceMgr", $"{packageName} 初始化成功");
            callback?.Invoke(true);
        };
    }

    public void InitHostPackage(ResourcePackageName packageNameEnum, Action<bool> callback, bool cdnOnly = false)
        => InitHostPackage(packageNameEnum.ToPackageString(), callback, cdnOnly);

    public async Task UnloadHostPackageAsync(ResourcePackageName packageNameEnum)
    {
        await UnloadHostPackageAsync(packageNameEnum.ToPackageString());
    }

    public async Task UnloadHostPackageAsync(string packageName)
    {
        var package = GetPackage(packageName);
        if (package == null)
        {
            return;
        }

        var destroyOp = package.DestroyAsync();
        if (!destroyOp.IsDone)
        {
            await destroyOp.Task;
        }

        YooAssets.RemovePackage(packageName);
        _packages.Remove(packageName);
        GameDebug.Log("ResourceMgr", $"{packageName} 已卸载");
    }

    private bool TryReuseHostPackage(string packageName, Action<bool> callback)
    {
        if (_packages.TryGetValue(packageName, out var cached) && cached != null)
        {
            callback?.Invoke(true);
            return true;
        }

        var existing = YooAssets.TryGetPackage(packageName);
        if (existing == null)
        {
            return false;
        }

        _packages[packageName] = existing;
        callback?.Invoke(true);
        return true;
    }

    private void InitOfflinePackage(string packageName, Action<bool, ResourcePackage> callback)
    {
        var package = YooAssets.CreatePackage(packageName);
        InitializeParameters initParams;

#if UNITY_EDITOR
        if (LaunchRuntimeResourceUtil.UseRuntimeResourcePipeline)
        {
            initParams = new OfflinePlayModeParameters
            {
                BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters()
            };
        }
        else
        {
            try
            {
                var buildResult = EditorSimulateModeHelper.SimulateBuild(packageName);
                initParams = new EditorSimulateModeParameters
                {
                    EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory)
                };
            }
            catch (Exception ex)
            {
                GameDebug.LogWarning("ResourceMgr", $"{packageName} 未配置 YooAsset 收集器，跳过离线模拟初始化：{ex.Message}");
                YooAssets.RemovePackage(packageName);
                callback?.Invoke(false, null);
                return;
            }
        }
#else
        initParams = new OfflinePlayModeParameters
        {
            BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters()
        };
#endif

        package.InitializeAsync(initParams).Completed += initHandle =>
        {
            if (initHandle.Status != EOperationStatus.Succeed)
            {
                GameDebug.LogWarning("ResourceMgr", $"{packageName} 初始化失败: {initHandle.Error}");
                callback?.Invoke(false, null);
                return;
            }

            package.RequestPackageVersionAsync().Completed += versionHandle =>
            {
                if (versionHandle.Status != EOperationStatus.Succeed)
                {
                    GameDebug.LogWarning("ResourceMgr", $"{packageName} 获取版本失败: {versionHandle.Error}");
                    callback?.Invoke(false, null);
                    return;
                }

                var version = ((RequestPackageVersionOperation)versionHandle).PackageVersion;
                package.UpdatePackageManifestAsync(version).Completed += manifestHandle =>
                {
                    if (manifestHandle.Status != EOperationStatus.Succeed)
                    {
                        GameDebug.LogWarning("ResourceMgr", $"{packageName} 更新清单失败: {manifestHandle.Error}");
                        callback?.Invoke(false, null);
                        return;
                    }

                    GameDebug.Log("ResourceMgr", $"{packageName} 初始化成功");
                    callback?.Invoke(true, package);
                };
            };
        };
    }

    private InitializeParameters CreateHostPlayModeParameters(string packageName, bool cdnOnly)
    {
        var cacheParams = FileSystemParameters.CreateDefaultCacheFileSystemParameters(
            new RemoteServices(CdnBase, packageName));

        if (cdnOnly)
        {
            return new HostPlayModeParameters
            {
                CacheFileSystemParameters = cacheParams
            };
        }

        return new HostPlayModeParameters
        {
            BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
            CacheFileSystemParameters = cacheParams
        };
    }

    private sealed class RemoteServices : IRemoteServices
    {
        private readonly string packageBaseUrl;

        public RemoteServices(string cdnBase, string packageName)
        {
            packageBaseUrl = $"{cdnBase.TrimEnd('/')}/{PlatformDir}/{packageName}";
        }

        public string GetRemoteMainURL(string fileName) => $"{packageBaseUrl}/{fileName}";
        public string GetRemoteFallbackURL(string fileName) => $"{packageBaseUrl}/{fileName}";
    }
}
