using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>一个 YooAsset 地址的休眠实例及其唯一预制体引用。</summary>
internal sealed class OnePool : IDisposable
{
    const int MaxIdle = 12;

    string address;
    ResourcePackageName package;
    ResourceService resourceService;
    Transform idleRoot;
    Stack<GameObject> idle = new Stack<GameObject>();
    List<ParticleSystem> particles = new List<ParticleSystem>();
    GameObject prefab;
    Task<GameObject> pendingLoad;
    bool disposed;
    internal int IdleCount => idle.Count;
    internal int CreatedCount { get; private set; }
    internal int ReusedCount { get; private set; }

    internal void Prewarm(int count)
    {
        var source = Load();
        if (!source) throw new InvalidOperationException("缺少对象池预热资源：" + address);
        while (idle.Count < Math.Min(MaxIdle, count))
        {
            var instance = ResourceService.InstantiateInactivePrefab(source, idleRoot);
            CreatedCount++;
            Return(instance);
        }
    }

    internal OnePool(string address, ResourcePackageName package, Transform idleRoot)
    {
        this.address = address;
        this.package = package;
        this.idleRoot = idleRoot;
        resourceService = MVC.ResourceService;
    }

    internal GameObject Load()
    {
        if (pendingLoad is { IsCompleted: false })
        {
            throw new InvalidOperationException("Await prefab preload before spawning synchronously: " + address);
        }
        if (!prefab)
        {
            prefab = resourceService.LoadAsset<GameObject>(address, package);
        }
        return prefab;
    }

    internal Task<GameObject> PreloadAsync()
    {
        if (prefab)
        {
            return Task.FromResult(prefab);
        }
        if (pendingLoad is not { IsCompleted: false })
        {
            pendingLoad = LoadPrefabAsync();
        }
        return pendingLoad;
    }

    async Task<GameObject> LoadPrefabAsync()
    {
        var loaded = await resourceService.LoadAssetAsync<GameObject>(address, package);
        if (disposed)
        {
            // 场景可能先退出；迟到的加载引用只能由原请求释放。
            if (loaded)
            {
                resourceService.Release(address, package);
            }
            return null;
        }
        prefab = loaded;
        return prefab;
    }

    internal GameObject Take(GameObject source)
    {
        while (idle.Count > 0)
        {
            var instance = idle.Pop();
            if (instance)
            {
                ReusedCount++;
                var template = source.transform;
                var target = instance.transform;
                target.SetLocalPositionAndRotation(template.localPosition, template.localRotation);
                target.localScale = template.localScale;
                return instance;
            }
        }
        CreatedCount++;
        return ResourceService.InstantiateInactivePrefab(source, idleRoot);
    }

    internal void Return(GameObject instance)
    {
        instance.SetActive(false);
        if (disposed || !idleRoot || idle.Count >= MaxIdle)
        {
            UnityEngine.Object.Destroy(instance);
            return;
        }
        instance.transform.SetParent(idleRoot, false);
        instance.GetComponentsInChildren(true, particles);
        for (var i = 0; i < particles.Count; i++)
        {
            particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        particles.Clear();
        idle.Push(instance);
    }

    /// <summary>释放本地址持有的引用；休眠节点由池组统一销毁。</summary>
    public void Dispose()
    {
        disposed = true;
        idle.Clear();
        if (prefab)
        {
            resourceService.Release(address, package);
            prefab = null;
        }
    }
}
