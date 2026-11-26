using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 全局 SO 配置加载与缓存。每种类型对应一个包和地址，由本服务统一持有资源引用。
/// 沿用旧 ClientExpress SOConfigService 的职责，补充地址校验及加载期间卸载处理。
/// LaunchSOConfig 仍由 AOT 启动层加载。本服务的 API 在 Unity 主线程调用。
/// </summary>
public sealed class SOConfigService : IService
{
    private Dictionary<Type, SOConfigCacheEntry> entries = new Dictionary<Type, SOConfigCacheEntry>();

    /// <summary>加载全局配置；并发请求共用加载任务，失败后允许重试。</summary>
    public async Task<T> GetAsync<T>(string location, ResourcePackageName packageName) where T : ScriptableObject
    {
        if (TryFind<T>(location, packageName, out var entry))
        {
            return (T)await entry.Completion.Task;
        }

        entry = CreateEntry<T>(location, packageName);
        try
        {
            var asset = await MVC.ResourceService.LoadAssetAsync<T>(location, packageName);
            CompleteLoad(entry, asset);
        }
        catch (Exception exception)
        {
            RemoveIfCurrent<T>(entry);
            entry.Completion.TrySetException(exception);
        }

        return (T)await entry.Completion.Task;
    }

    /// <summary>同步加载配置。若同类型正在异步加载，应改为等待 GetAsync，避免阻塞主线程。</summary>
    public T Get<T>(string location, ResourcePackageName packageName) where T : ScriptableObject
    {
        if (TryFind<T>(location, packageName, out var entry))
        {
            if (!entry.Completion.Task.IsCompleted)
            {
                throw new InvalidOperationException($"SOConfig is loading asynchronously: {typeof(T).Name}. Use GetAsync.");
            }
            return (T)entry.Completion.Task.GetAwaiter().GetResult();
        }

        entry = CreateEntry<T>(location, packageName);
        try
        {
            CompleteLoad(entry, MVC.ResourceService.LoadAsset<T>(location, packageName));
        }
        catch
        {
            RemoveIfCurrent<T>(entry);
            throw;
        }
        return (T)entry.Asset;
    }

    /// <summary>仅查询已完成加载的配置；未加载或正在加载时返回 false。</summary>
    public bool TryGetCached<T>(out T config) where T : ScriptableObject
    {
        if (entries.TryGetValue(typeof(T), out var entry) && entry.Acquired)
        {
            config = (T)entry.Asset;
            return true;
        }
        config = null;
        return false;
    }

    /// <summary>卸载指定类型；未完成的加载在返回后释放引用，并以取消结束原请求。</summary>
    public void Unload<T>() where T : ScriptableObject
    {
        if (entries.TryGetValue(typeof(T), out var entry))
        {
            entries.Remove(typeof(T));
            Invalidate(entry);
        }
    }

    /// <summary>卸载全部配置；业务模块须先结束使用已有配置引用。</summary>
    public void UnloadAll()
    {
        foreach (var entry in entries.Values)
        {
            Invalidate(entry);
        }
        entries.Clear();
    }

    private bool TryFind<T>(string location, ResourcePackageName packageName, out SOConfigCacheEntry entry)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new ArgumentException("SOConfig location is empty.", nameof(location));
        }
        if (!entries.TryGetValue(typeof(T), out entry))
        {
            return false;
        }
        if (entry.Location != location || entry.PackageName != packageName)
        {
            throw new InvalidOperationException($"SOConfig {typeof(T).Name} already uses {entry.PackageName}/{entry.Location}; requested {packageName}/{location}.");
        }
        return true;
    }

    private SOConfigCacheEntry CreateEntry<T>(string location, ResourcePackageName packageName)
    {
        var entry = new SOConfigCacheEntry { Location = location, PackageName = packageName };
        entries.Add(typeof(T), entry);
        return entry;
    }

    private void CompleteLoad(SOConfigCacheEntry entry, ScriptableObject asset)
    {
        // ResourceService 的加载失败语义是返回空资源，在本配置 API 边界转成异常。
        if (!asset)
        {
            throw new InvalidOperationException($"SOConfig load failed: {entry.PackageName}/{entry.Location}");
        }
        if (entry.Invalidated)
        {
            MVC.ResourceService.Release(entry.Location, entry.PackageName);
            entry.Completion.TrySetCanceled();
            return;
        }
        entry.Asset = asset;
        entry.Acquired = true;
        entry.Completion.TrySetResult(asset);
    }

    private void RemoveIfCurrent<T>(SOConfigCacheEntry entry)
    {
        if (entries.TryGetValue(typeof(T), out var current) && ReferenceEquals(current, entry))
        {
            entries.Remove(typeof(T));
        }
    }

    private void Invalidate(SOConfigCacheEntry entry)
    {
        entry.Invalidated = true;
        if (entry.Acquired)
        {
            MVC.ResourceService.Release(entry.Location, entry.PackageName);
            entry.Acquired = false;
        }
    }
}
