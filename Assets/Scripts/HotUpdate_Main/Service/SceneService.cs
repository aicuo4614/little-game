using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

/// <summary>
/// 场景加载与卸载：通过 YooAsset（与 HybridCLR 热更资源管线一致）。
/// 通过 <c>MVC.SceneService</c> 获取。
/// </summary>
public sealed class SceneService : IService
{
    /// <summary>按场景 handle 缓存的直接根节点 activeSelf 快照（隐藏时写入，恢复后清除）。</summary>
    private Dictionary<int, SceneRootsActiveSnapshot> sceneRootsActiveSnapshots =
        new Dictionary<int, SceneRootsActiveSnapshot>();

    /// <summary>
    /// 异步加载场景（资源包内地址，例如 <c>MainScene/MainScene</c>、<c>Scene/Low/RoomScene/RoomScene</c>）。
    /// ScenePackage / ResPackage 场景会先按地址按需下载（含依赖），再加载；进度前半段为下载、后半段为加载。
    /// </summary>
    public async Task<SceneHandle> LoadSceneAsync(
        string sceneLocation,
        ResourcePackageName packageName = ResourcePackageName.MainPackage,
        LoadSceneMode loadMode = LoadSceneMode.Single,
        Action<float> onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sceneLocation))
        {
            throw new ArgumentException("sceneLocation 为空", nameof(sceneLocation));
        }

        var package = YooAssets.GetPackage(packageName.ToPackageString());
        if (package == null)
        {
            var err = $"资源包未初始化或不存在: {packageName.ToPackageString()}";
            GameDebug.LogError("SceneService", $"{err}");
            throw new InvalidOperationException(err);
        }

        var didDownload = false;
        var needsRemoteDownload = packageName == ResourcePackageName.ScenePackage
            || packageName == ResourcePackageName.ResPackage;
        if (needsRemoteDownload
            && MVC.ResourceService.CheckLocationValid(sceneLocation, packageName)
            && package.IsNeedDownloadFromRemote(sceneLocation))
        {
            didDownload = await ResourceDownloadUtil.EnsureByLocationAsync(
                sceneLocation,
                packageName,
                p =>
                {
                    onProgress?.Invoke(Mathf.Clamp01(p * 0.5f));
                },
                recursiveDownload: true);
        }
        var handle = package.LoadSceneAsync(sceneLocation, loadMode);

        var loadTask = handle.Task;
        if (loadTask == null)
        {
            throw new InvalidOperationException("场景加载句柄 Task 不可用");
        }

        if (onProgress != null)
        {
            var reportTask = ReportProgressWhileSceneHandleLoadingAsync(
                handle,
                p =>
                {
                    if (didDownload)
                    {
                        onProgress.Invoke(0.5f + Mathf.Clamp01(p) * 0.5f);
                    }
                    else
                    {
                        onProgress.Invoke(Mathf.Clamp01(p));
                    }
                },
                cancellationToken);
            await Task.WhenAll(loadTask, reportTask);
        }
        else
        {
            await loadTask;
        }

        if (!handle.IsValid)
        {
            throw new InvalidOperationException("场景加载句柄无效");
        }

        if (handle.Status != EOperationStatus.Succeed)
        {
            var err = string.IsNullOrEmpty(handle.LastError) ? "加载场景失败" : handle.LastError;
            GameDebug.LogError("SceneService", $"{err}");
            throw new InvalidOperationException(err);
        }

        onProgress?.Invoke(1f);
        return handle;
    }

    /// <summary>
    /// 异步卸载由 <see cref="SceneHandle"/> 表示的场景（一般为附加加载的子场景）。
    /// 不能卸载 <see cref="LoadSceneMode.Single"/> 下的主场景；请改用加载其他主场景切换。
    /// </summary>
    public async Task UnloadSceneAsync(
        SceneHandle handle,
        Action<float> onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (handle == null)
            throw new ArgumentNullException(nameof(handle));
        if (!handle.IsValid)
            throw new InvalidOperationException("场景句柄无效");

        // 卸载前清快照，避免句柄失效后残留
        ClearSceneRootsVisibleSnapshot(handle.SceneObject);

        var op = handle.UnloadAsync();
        if (op == null)
            throw new InvalidOperationException("UnloadAsync 返回 null");

        var unloadTask = op.Task;
        if (unloadTask == null)
            throw new InvalidOperationException("卸载操作 Task 不可用");

        if (onProgress != null)
        {
            var reportTask = ReportProgressWhileUnloadAsync(op, onProgress, cancellationToken);
            await Task.WhenAll(unloadTask, reportTask);
        }
        else
            await unloadTask;

        if (op.Status != EOperationStatus.Succeed)
        {
            var err = string.IsNullOrEmpty(op.Error) ? "卸载场景失败" : op.Error;
            GameDebug.LogError("SceneService", $"{err}");
            throw new InvalidOperationException(err);
        }

        onProgress?.Invoke(1f);
    }

    /// <summary>
    /// 卸载 Yoo 资源包内引用计数为零的 Bundle（含依赖链迭代，默认最多 10 轮）。
    /// 须在应用层 <see cref="ResourceService.Release"/> 之后调用；只卸无人引用的 Bundle，不影响仍被持有的资源。
    /// </summary>
    public async Task UnloadYooUnusedBundlesAsync(ResourcePackageName packageName = ResourcePackageName.MainPackage)
    {
        var package = YooAssets.GetPackage(packageName.ToPackageString());
        if (package == null)
        {
            GameDebug.LogWarning("SceneService", $"UnloadYooUnusedBundles: 包不存在 {packageName}");
            return;
        }

        var op = package.UnloadUnusedAssetsAsync();
        var unloadTask = op.Task;
        if (unloadTask != null)
        {
            await unloadTask;
        }
        else
        {
            while (!op.IsDone)
            {
                await Task.Yield();
            }
        }

        if (op.Status != EOperationStatus.Succeed)
        {
            var err = string.IsNullOrEmpty(op.Error) ? "Yoo UnloadUnusedAssets 失败" : op.Error;
            GameDebug.LogError("SceneService", $"{packageName} {err}");
            throw new InvalidOperationException(err);
        }

        GameDebug.Log("SceneService", $"Yoo UnloadUnusedAssets 完成 {packageName}");
    }

    /// <summary>
    /// 指定 Yoo 包内 ref=0 的 Bundle 卸载后，再 Unity UUA。
    /// 须在应用层 <see cref="ResourceService.Release"/> 与 <see cref="UnloadSceneAsync"/> 之后调用。
    /// </summary>
    public async Task UnloadYooPackageAndUnityAsync(ResourcePackageName packageName)
    {
        await UnloadYooUnusedBundlesAsync(packageName);
        await UnloadUnusedAssetsAsync();
    }

    /// <summary>
    /// 卸场景并释放句柄后，回收已无引用的 Unity 原生资源。
    /// 应在各 Control <c>ReleaseResources</c>、<see cref="UnloadYooUnusedBundlesAsync"/> 与 <see cref="UnloadSceneAsync"/> 之后调用。
    /// </summary>
    public async Task UnloadUnusedAssetsAsync()
    {
        var op = Resources.UnloadUnusedAssets();
        while (!op.isDone)
        {
            await Task.Yield();
        }

        GameDebug.Log("SceneService", "UnloadUnusedAssets 完成");
    }

    /// <summary>
    /// 显隐场景的直接根节点。不卸载场景。
    /// <paramref name="visible"/> 为 false：记录各根当前 <c>activeSelf</c> 后全部 <c>SetActive(false)</c>；
    /// 为 true：按记录恢复，并清除该场景快照。已隐藏再次 false / 无快照时 true 均为空操作。
    /// </summary>
    public void SetSceneRootsVisible(Scene scene, bool visible)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            GameDebug.LogWarning("SceneService", "SetSceneRootsVisible: 场景无效或未加载");
            return;
        }

        var key = scene.handle;
        if (visible)
        {
            RestoreSceneRootsVisible(key);
            return;
        }

        HideSceneRootsVisible(scene, key);
    }

    /// <summary>丢弃指定场景的根节点显隐快照（不改变当前 active）。</summary>
    public void ClearSceneRootsVisibleSnapshot(Scene scene)
    {
        if (!scene.IsValid())
        {
            return;
        }

        sceneRootsActiveSnapshots.Remove(scene.handle);
    }

    void HideSceneRootsVisible(Scene scene, int key)
    {
        if (sceneRootsActiveSnapshots.ContainsKey(key))
        {
            return;
        }

        var roots = scene.GetRootGameObjects();
        var snapshot = new SceneRootsActiveSnapshot();
        for (var i = 0; i < roots.Length; i++)
        {
            var root = roots[i];
            snapshot.Roots.Add(root);
            snapshot.WasActive.Add(root.activeSelf);
            if (root.activeSelf)
            {
                root.SetActive(false);
            }
        }

        sceneRootsActiveSnapshots[key] = snapshot;
        GameDebug.Log("SceneService", $"场景根节点已隐藏: {scene.name}, roots={roots.Length}");
    }

    void RestoreSceneRootsVisible(int key)
    {
        if (!sceneRootsActiveSnapshots.TryGetValue(key, out var snapshot))
        {
            return;
        }

        sceneRootsActiveSnapshots.Remove(key);
        for (var i = 0; i < snapshot.Roots.Count; i++)
        {
            var root = snapshot.Roots[i];
            if (root == null)
            {
                continue;
            }

            root.SetActive(snapshot.WasActive[i]);
        }

        GameDebug.Log("SceneService", $"场景根节点已按快照恢复: count={snapshot.Roots.Count}");
    }

    /// <summary>单场景直接根节点在隐藏前的 activeSelf 记录。</summary>
    private sealed class SceneRootsActiveSnapshot
    {
        public List<GameObject> Roots = new List<GameObject>();
        public List<bool> WasActive = new List<bool>();
    }

    private static async Task ReportProgressWhileSceneHandleLoadingAsync(
        SceneHandle handle,
        Action<float> onProgress,
        CancellationToken cancellationToken)
    {
        while (!handle.IsDone)
        {
            onProgress.Invoke(Mathf.Clamp01(handle.Progress));
            await Task.Delay(16, cancellationToken);
        }
    }

    private static async Task ReportProgressWhileUnloadAsync(
        UnloadSceneOperation op,
        Action<float> onProgress,
        CancellationToken cancellationToken)
    {
        while (!op.IsDone)
        {
            onProgress.Invoke(Mathf.Clamp01(op.Progress));
            await Task.Delay(16, cancellationToken);
        }
    }
}
