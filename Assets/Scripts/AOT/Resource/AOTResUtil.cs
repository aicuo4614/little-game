using System;
using System.Threading.Tasks;
using UnityEngine;
using YooAsset;

/// <summary>
/// AOT 侧资源加载（热更 DLL 就绪前可用）。路径为 YooAsset 地址，非 <c>Assets/...</c>。
/// </summary>
public class AOTResUtil
{
    /// <summary>回调式加载资源。</summary>
    public static void LoadAsset<T>(string path, Action<T> callback, ResourcePackageName packageName = ResourcePackageName.MainPackage) where T : UnityEngine.Object
    {
        var package = YooAssets.GetPackage(packageName.ToPackageString());
        package.LoadAssetAsync<T>(path).Completed += handle =>
        {
            if (handle.Status == EOperationStatus.Succeed)
            {
                callback(handle.GetAssetObject<T>());
            }
            else
            {
                GameDebug.LogError("AOTResUtil", $"资源加载失败：{packageName}/{path} {handle.LastError}");
                callback(null);
            }
        };
    }

    /// <summary>异步加载资源，可直接 await。</summary>
    public static Task<T> LoadAssetAsync<T>(string path, ResourcePackageName packageName = ResourcePackageName.MainPackage) where T : UnityEngine.Object
    {
        var tcs = new TaskCompletionSource<T>();
        LoadAsset<T>(path, result => tcs.TrySetResult(result), packageName);
        return tcs.Task;
    }

    /// <summary>同步加载资源（FairyGUI <c>LoadResource</c> 等同步回调用）。</summary>
    public static T LoadAssetSync<T>(string path, ResourcePackageName packageName = ResourcePackageName.MainPackage) where T : UnityEngine.Object
    {
        var package = YooAssets.GetPackage(packageName.ToPackageString());
        var handle = package.LoadAssetSync<T>(path);
        if (handle.Status != EOperationStatus.Succeed)
        {
            GameDebug.LogError("AOTResUtil", $"资源加载失败：{packageName}/{path} {handle.LastError}");
            return null;
        }

        return handle.GetAssetObject<T>();
    }

    /// <summary>回调式加载并实例化 Prefab。</summary>
    public static void LoadPrefab(string path, Action<GameObject> callback, Transform parent, ResourcePackageName packageName = ResourcePackageName.MainPackage)
    {
        LoadAsset<GameObject>(path, prefab =>
        {
            if (prefab != null && parent != null)
            {
                var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
                callback?.Invoke(instance);
            }
            else
            {
                callback?.Invoke(null);
            }
        }, packageName);
    }

    /// <summary>异步加载并实例化 Prefab，可直接 await。</summary>
    public static Task<GameObject> LoadPrefabAsync(string path, Transform parent, ResourcePackageName packageName = ResourcePackageName.MainPackage)
    {
        var tcs = new TaskCompletionSource<GameObject>();
        LoadPrefab(path, result => tcs.TrySetResult(result), parent, packageName);
        return tcs.Task;
    }

    /// <summary>回调式从 ModulePackage 加载 Prefab。</summary>
    public static void LoadModulePrefab(string path, Action<GameObject> callback, Transform parent)
    {
        LoadPrefab(path, callback, parent, ResourcePackageName.ModulePackage);
    }

    /// <summary>异步从 ModulePackage 加载 Prefab，可直接 await。</summary>
    public static Task<GameObject> LoadModulePrefabAsync(string path, Transform parent)
    {
        return LoadPrefabAsync(path, parent, ResourcePackageName.ModulePackage);
    }
}
