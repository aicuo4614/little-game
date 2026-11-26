using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;

/// <summary>
/// YooAsset 资源加载服务（对齐 Demo ResourceService）：
/// 按「包 + 地址」持有 AssetHandle 并做引用计数；若包尚未初始化，平滑兼容 Resources 路径加载。
/// 通过 <c>MVC.ResourceService</c> 获取。
/// </summary>
public sealed class ResourceService : IService
{
    private sealed class AssetEntry
    {
        public AssetHandle Handle;
        public UnityEngine.Object FallbackAsset;
        public int RefCount;
        public TaskCompletionSource<bool> LoadTcs;
    }

    private readonly Dictionary<string, AssetEntry> entries = new Dictionary<string, AssetEntry>();

    public bool CheckLocationValid(string path, ResourcePackageName packageName = ResourcePackageName.ResPackage)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var package = YooAssets.TryGetPackage(packageName.ToPackageString());
        if (package != null && package.CheckLocationValid(path))
        {
            return true;
        }

        return Resources.Load(path) != null;
    }

    public int GetRefCount(string path, ResourcePackageName packageName = ResourcePackageName.ResPackage)
    {
        return entries.TryGetValue(MakeKey(packageName, path), out var entry) ? entry.RefCount : 0;
    }

    public void CopyEntriesSnapshot(List<ResourceEntrySnapshot> dest)
    {
        dest.Clear();
        foreach (var kv in entries)
        {
            var entry = kv.Value;
            if (!TrySplitKey(kv.Key, out var packageName, out var path))
            {
                continue;
            }

            dest.Add(new ResourceEntrySnapshot
            {
                Key = kv.Key,
                PackageName = packageName,
                Path = path,
                RefCount = entry.RefCount,
                HandleValid = (entry.Handle != null && entry.Handle.IsValid) || entry.FallbackAsset != null,
            });
        }
    }

    public int EntryCount => entries.Count;

    public T LoadAsset<T>(string path, ResourcePackageName packageName = ResourcePackageName.ResPackage)
        where T : UnityEngine.Object
    {
        if (!TryBeginLoad(path, packageName, out var key, out var package, out var cached))
        {
            return cached as T;
        }

        if (package == null || !package.CheckLocationValid(path))
        {
            return RegisterFallbackAsset<T>(key, path, packageName);
        }

        if (entries.TryGetValue(key, out var inflight)
            && inflight.Handle != null
            && inflight.Handle.IsValid
            && !inflight.Handle.IsDone)
        {
            inflight.Handle.WaitForAsyncComplete();
            return AcquireFromReadyEntry<T>(key, inflight, packageName, path);
        }

        var handle = package.LoadAssetSync<T>(path);
        return RegisterCompletedHandle<T>(key, handle, packageName, path);
    }

    public async Task<T> LoadAssetAsync<T>(string path, ResourcePackageName packageName = ResourcePackageName.ResPackage)
        where T : UnityEngine.Object
    {
        if (!TryBeginLoad(path, packageName, out var key, out var package, out var cached))
        {
            return cached as T;
        }

        if (package == null || !package.CheckLocationValid(path))
        {
            return RegisterFallbackAsset<T>(key, path, packageName);
        }

        if (entries.TryGetValue(key, out var inflight)
            && inflight.LoadTcs != null
            && !inflight.LoadTcs.Task.IsCompleted)
        {
            await inflight.LoadTcs.Task;
            return TakeCachedAfterAwait<T>(key);
        }

        var entry = new AssetEntry
        {
            RefCount = 0,
            LoadTcs = new TaskCompletionSource<bool>(),
        };
        entries[key] = entry;

        try
        {
            await ResourceDownloadUtil.EnsureByLocationAsync(path, packageName, null, false);
            var handle = package.LoadAssetAsync<T>(path);
            entry.Handle = handle;
            if (!handle.IsDone)
            {
                await handle.Task;
            }

            return AcquireFromReadyEntry<T>(key, entry, packageName, path);
        }
        catch (Exception)
        {
            if (entry.Handle != null && entry.Handle.IsValid)
            {
                entry.Handle.Release();
            }

            entries.Remove(key);
            entry.LoadTcs.TrySetResult(false);
            throw;
        }
    }

    public GameObject LoadPrefab(
        string path,
        Transform parent,
        ResourcePackageName packageName = ResourcePackageName.ResPackage)
    {
        var prefab = LoadAsset<GameObject>(path, packageName);
        return prefab != null ? InstantiatePrefab(prefab, parent) : null;
    }

    public async Task<GameObject> LoadPrefabAsync(
        string path,
        Transform parent,
        ResourcePackageName packageName = ResourcePackageName.ResPackage)
    {
        var prefab = await LoadAssetAsync<GameObject>(path, packageName);
        return prefab != null ? InstantiatePrefab(prefab, parent) : null;
    }

    public void Release(string path, ResourcePackageName packageName = ResourcePackageName.ResPackage)
    {
        var key = MakeKey(packageName, path);
        if (!entries.TryGetValue(key, out var entry))
        {
            return;
        }

        entry.RefCount--;
        if (entry.RefCount > 0)
        {
            return;
        }

        if (entry.Handle != null && entry.Handle.IsValid)
        {
            entry.Handle.Release();
        }

        entries.Remove(key);
    }

    public void LoadRemoteText(string url, Action<string> completeCallback, Action<string> failedCallback = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            failedCallback?.Invoke("url 不能为空。");
            completeCallback?.Invoke(null);
            return;
        }

        CoroutineRunner.Start(LoadRemoteRoutine(url, completeCallback, failedCallback, h => h.text, "文本"));
    }

    public void LoadRemoteBytes(string url, Action<byte[]> completeCallback, Action<string> failedCallback = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            failedCallback?.Invoke("url 不能为空。");
            completeCallback?.Invoke(null);
            return;
        }

        CoroutineRunner.Start(LoadRemoteRoutine(url, completeCallback, failedCallback, h => h.data, "字节"));
    }

    private bool TryBeginLoad(
        string path,
        ResourcePackageName packageName,
        out string key,
        out ResourcePackage package,
        out UnityEngine.Object cached)
    {
        key = null;
        package = null;
        cached = null;

        if (string.IsNullOrEmpty(path))
        {
            GameDebug.LogError("ResourceService", "path 为空");
            return false;
        }

        key = MakeKey(packageName, path);
        if (entries.TryGetValue(key, out var existing))
        {
            if (existing.FallbackAsset != null)
            {
                existing.RefCount++;
                cached = existing.FallbackAsset;
                return false;
            }

            if (existing.Handle != null
                && existing.Handle.IsValid
                && existing.Handle.IsDone
                && existing.Handle.Status == EOperationStatus.Succeed)
            {
                existing.RefCount++;
                cached = existing.Handle.AssetObject;
                return false;
            }
        }

        package = YooAssets.TryGetPackage(packageName.ToPackageString());
        return true;
    }

    private T RegisterFallbackAsset<T>(string key, string path, ResourcePackageName packageName)
        where T : UnityEngine.Object
    {
        var asset = Resources.Load<T>(path);
        if (asset == null)
        {
            GameDebug.LogWarning("ResourceService", $"资源加载失败（YooAsset 与 Resources 均未命中）：{packageName}/{path}");
            return null;
        }

        var entry = new AssetEntry
        {
            FallbackAsset = asset,
            RefCount = 1,
            LoadTcs = new TaskCompletionSource<bool>(),
        };
        entry.LoadTcs.TrySetResult(true);
        entries[key] = entry;
        return asset;
    }

    private T TakeCachedAfterAwait<T>(string key)
        where T : UnityEngine.Object
    {
        if (entries.TryGetValue(key, out var ready))
        {
            if (ready.FallbackAsset is T fallback)
            {
                ready.RefCount++;
                return fallback;
            }

            if (ready.Handle != null && ready.Handle.IsValid && ready.Handle.Status == EOperationStatus.Succeed)
            {
                ready.RefCount++;
                return ready.Handle.GetAssetObject<T>();
            }
        }

        return null;
    }

    private T AcquireFromReadyEntry<T>(string key, AssetEntry entry, ResourcePackageName packageName, string path)
        where T : UnityEngine.Object
    {
        var handle = entry.Handle;
        if (handle == null || !handle.IsValid || handle.Status != EOperationStatus.Succeed)
        {
            var err = handle != null ? handle.LastError : "handle invalid";
            GameDebug.LogError("ResourceService", $"资源加载失败：{packageName}/{path} {err}");
            if (handle != null && handle.IsValid)
            {
                handle.Release();
            }

            entries.Remove(key);
            entry.LoadTcs?.TrySetResult(false);
            return null;
        }

        if (entry.RefCount == 0)
        {
            entry.RefCount = 1;
            entry.LoadTcs?.TrySetResult(true);
        }
        else
        {
            entry.RefCount++;
        }

        return handle.GetAssetObject<T>();
    }

    private T RegisterCompletedHandle<T>(string key, AssetHandle handle, ResourcePackageName packageName, string path)
        where T : UnityEngine.Object
    {
        if (handle.Status != EOperationStatus.Succeed)
        {
            GameDebug.LogError("ResourceService", $"资源加载失败：{packageName}/{path} {handle.LastError}");
            handle.Release();
            return null;
        }

        var entry = new AssetEntry
        {
            Handle = handle,
            RefCount = 1,
            LoadTcs = new TaskCompletionSource<bool>(),
        };
        entry.LoadTcs.TrySetResult(true);
        entries[key] = entry;
        return handle.GetAssetObject<T>();
    }

    private static string MakeKey(ResourcePackageName packageName, string path)
    {
        return packageName.ToPackageString() + "|" + path;
    }

    private static bool TrySplitKey(string key, out string packageName, out string path)
    {
        packageName = null;
        path = null;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var sep = key.IndexOf('|');
        if (sep <= 0 || sep >= key.Length - 1)
        {
            return false;
        }

        packageName = key.Substring(0, sep);
        path = key.Substring(sep + 1);
        return true;
    }

    private static GameObject InstantiatePrefab(GameObject prefab, Transform parent)
    {
        var hideRoot = new GameObject("~PrefabSpawnHide");
        hideRoot.SetActive(false);
        var instance = InstantiateInactivePrefab(prefab, hideRoot.transform);
        instance.transform.SetParent(parent, false);
        UnityEngine.Object.Destroy(hideRoot);
        return instance;
    }

    internal static GameObject InstantiateInactivePrefab(GameObject prefab, Transform inactiveParent)
    {
        return UnityEngine.Object.Instantiate(prefab, inactiveParent, false);
    }

    private static IEnumerator LoadRemoteRoutine<T>(
        string url,
        Action<T> completeCallback,
        Action<string> failedCallback,
        Func<DownloadHandler, T> converter,
        string resourceType)
    {
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            completeCallback?.Invoke(converter(req.downloadHandler));
            yield break;
        }

        var error = $"远程{resourceType}下载失败: url={url}, err={req.error}";
        GameDebug.LogError("ResourceService", error);
        failedCallback?.Invoke(error);
    }
}
