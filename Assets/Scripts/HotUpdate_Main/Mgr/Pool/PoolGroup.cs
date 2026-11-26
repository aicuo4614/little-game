using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>一个房间或战斗的池组，按 YooAsset 包与地址区分预制体。</summary>
public sealed class PoolGroup : IDisposable
{
    Transform idleRoot;
    Dictionary<(ResourcePackageName, string), OnePool> pools = new Dictionary<(ResourcePackageName, string), OnePool>();
    Dictionary<GameObject, OnePool> active = new Dictionary<GameObject, OnePool>();
    bool disposed;

    public int ActiveCount => active.Count;
    public int PoolCount => pools.Count;
    public int IdleCount => System.Linq.Enumerable.Sum(pools.Values, p => p.IdleCount);

    internal PoolGroup(Transform owner)
    {
        if (!owner)
        {
            throw new ArgumentException("A live pool owner is required.", nameof(owner));
        }
        var root = new GameObject("~PoolRoot");
        root.SetActive(false);
        idleRoot = root.transform;
        idleRoot.SetParent(owner, false);
    }

    /// <summary>异步预载一个地址，供后续同步 Spawn 使用。</summary>
    public Task<GameObject> PreloadAsync(string address, ResourcePackageName package)
    {
        return GetPool(address, package).PreloadAsync();
    }

    /// <summary>取出或创建实例，首次同步加载要求资源已在本地。</summary>
    /// <param name="address">无扩展名的 YooAsset 地址。</param>
    /// <param name="parent">实例的场景父节点。</param>
    /// <param name="package">资源所在的 YooAsset 包。</param>
    /// <param name="activate">是否按预制体的初始激活状态取出；false 时由调用方准备完毕后激活。</param>
    public GameObject Spawn(string address, Transform parent, ResourcePackageName package, bool activate = true)
    {
        var pool = GetPool(address, package);
        var prefab = pool.Load();
        if (!idleRoot || !parent || !prefab)
        {
            return null;
        }
        var instance = pool.Take(prefab);
        active.Add(instance, pool);
        // 在休眠根节点下完成复位，移动到目标父级时才可能触发 OnEnable。
        instance.SetActive(activate && prefab.activeSelf);
        instance.transform.SetParent(parent, false);
        return instance;
    }

    /// <summary>归还本组借出的对象。已归还或已清理的对象可重复传入。</summary>
    public void Despawn(GameObject instance)
    {
        if (!disposed && instance is not null && active.Remove(instance, out var pool) && instance)
        {
            pool.Return(instance);
        }
    }

    /// <summary>销毁全部实例并释放地址引用。在途加载完成后也会释放自己的引用。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        foreach (var instance in active.Keys)
        {
            if (instance)
            {
                instance.SetActive(false);
                UnityEngine.Object.Destroy(instance);
            }
        }
        active.Clear();
        if (idleRoot)
        {
            UnityEngine.Object.Destroy(idleRoot.gameObject);
        }
        foreach (var pool in pools.Values)
        {
            pool.Dispose();
        }
        pools.Clear();
    }

    OnePool GetPool(string address, ResourcePackageName package)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(PoolGroup));
        }
        if (string.IsNullOrEmpty(address))
        {
            throw new ArgumentException("A YooAsset address is required.", nameof(address));
        }
        var key = (package, address);
        if (!pools.TryGetValue(key, out var pool))
        {
            pool = new OnePool(address, package, idleRoot);
            pools.Add(key, pool);
        }
        return pool;
    }
}
