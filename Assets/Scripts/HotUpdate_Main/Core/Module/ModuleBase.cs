using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PureMVC.Patterns.Mediator;
using UnityEngine;

/// <summary>无打开参数的模块：<c>ModuleBase&lt;ModuleNoArgs&gt;</c>。</summary>
public struct ModuleNoArgs
{
}

/// <summary>
/// 模块运行时管理基类（对齐 Demo ModuleBase，底层适配 UGUI 窗口与 YooAsset 资源回收）。
/// </summary>
public abstract class ModuleBase : Mediator
{
    private sealed class WindowSlot
    {
        public string PrefabPath;
        public string AssetPath;
        public Type WindowType;
        public ModuleWindowBase Window;
        public bool IsLoading;
        public ModuleWindowState State;
        public List<Action<ModuleWindowBase>> PendingCallbacks = new List<Action<ModuleWindowBase>>();
    }

    private readonly Dictionary<string, WindowSlot> windowSlotByPath = new Dictionary<string, WindowSlot>(StringComparer.Ordinal);
    private readonly Dictionary<ModuleWindowBase, WindowSlot> windowSlotByInstance = new Dictionary<ModuleWindowBase, WindowSlot>();
    private bool isDestroying;

    protected ModuleBase() : base(NAME)
    {
    }

    public virtual bool NeedShowLoadingOnOpen => false;
    public virtual bool DestroyModuleWhenAllWindowsClosed => true;
    public virtual bool StretchWindowOnOpen => true;
    public virtual ResourcePackageName ModuleResourcePackage => ResourcePackageName.ModulePackage;
    public abstract string ModuleName { get; }
    public bool IsCreated { get; private set; }

    public virtual void Create()
    {
        MediatorName = ModuleName;
        MVC.Facade.RegisterMediator(this);
        IsCreated = true;
    }

    public void Open(object args, Action onOpened)
    {
        _ = OpenWithErrorHandlingAsync(args, onOpened);
    }

    private async Task OpenWithErrorHandlingAsync(object args, Action onOpened)
    {
        try
        {
            await OpenAsync(args, onOpened);
        }
        catch (Exception error)
        {
            GameDebug.LogError(ModuleName, error.ToString());
            ModuleMgr.Instance.DestroyModule(ModuleName);
            onOpened?.Invoke();
        }
    }

    protected virtual async Task OpenAsync(object args, Action onOpened)
    {
        await OnOpenAsync();
        onOpened?.Invoke();
    }

    protected virtual Task OnOpenAsync()
    {
        return Task.CompletedTask;
    }

    public virtual void Close()
    {
        foreach (var slot in windowSlotByPath.Values)
        {
            if (slot.Window != null && slot.State != ModuleWindowState.Closed)
            {
                slot.Window.CloseWindow();
            }
        }

        TryDestroyWhenAllWindowsClosed();
    }

    public virtual void Destroy()
    {
        isDestroying = true;
        foreach (var slot in windowSlotByPath.Values)
        {
            if (slot.Window != null)
            {
                windowSlotByInstance.Remove(slot.Window);
                UnityEngine.Object.Destroy(slot.Window.gameObject);
                slot.Window = null;
                slot.State = ModuleWindowState.Closed;
            }

            ReleaseSlotHandle(slot);
        }

        windowSlotByPath.Clear();
        windowSlotByInstance.Clear();

        if (MVC.Facade != null && MVC.Facade.HasMediator(MediatorName))
        {
            MVC.Facade.RemoveMediator(MediatorName);
        }

        IsCreated = false;
        isDestroying = false;
    }

    /// <summary>通过 YooAsset 异步加载 UGUI Prefab 窗口并走 Init → Open。</summary>
    protected async void OpenWindow<TWindow>(string prefabPath, Transform parent = null, Action<TWindow> onLoaded = null)
        where TWindow : ModuleWindowBase
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            GameDebug.LogError(ModuleName, "打开窗口失败：prefabPath 为空。");
            onLoaded?.Invoke(null);
            return;
        }

        if (windowSlotByPath.TryGetValue(prefabPath, out var existedSlot))
        {
            if (existedSlot.WindowType != typeof(TWindow))
            {
                GameDebug.LogError(ModuleName, $"窗口类型冲突 path={prefabPath}");
                onLoaded?.Invoke(null);
                return;
            }

            if (existedSlot.Window != null)
            {
                existedSlot.Window.InternalOpen();
                onLoaded?.Invoke(existedSlot.Window as TWindow);
                return;
            }

            if (existedSlot.IsLoading)
            {
                if (onLoaded != null)
                {
                    existedSlot.PendingCallbacks.Add(w => onLoaded(w as TWindow));
                }

                return;
            }
        }

        var slot = existedSlot ?? new WindowSlot
        {
            PrefabPath = prefabPath,
            WindowType = typeof(TWindow),
            State = ModuleWindowState.Closed,
        };
        slot.IsLoading = true;
        slot.Window = null;
        slot.PendingCallbacks.Clear();
        if (onLoaded != null)
        {
            slot.PendingCallbacks.Add(w => onLoaded(w as TWindow));
        }

        windowSlotByPath[prefabPath] = slot;

        var assetPath = $"{ModuleName}/{prefabPath}";
        var windowGo = await MVC.ResourceService.LoadPrefabAsync(assetPath, parent, ModuleResourcePackage);
        slot.IsLoading = false;

        if (windowGo == null)
        {
            HandleOpenWindowFailed(prefabPath, slot);
            return;
        }

        slot.AssetPath = assetPath;
        if (StretchWindowOnOpen)
        {
            ApplyWindowRootStretch(windowGo);
        }

        var window = windowGo.GetComponent<TWindow>();
        if (window == null)
        {
            GameDebug.LogError(ModuleName, $"窗口缺少组件 {typeof(TWindow).Name}，path={prefabPath}");
            UnityEngine.Object.Destroy(windowGo);
            HandleOpenWindowFailed(prefabPath, slot);
            return;
        }

        AttachWindowSlot(slot, window);
        InvokeWindowCallbacks(slot, window);
    }

    /// <summary>创建并托管程序构建的 UGUI 窗口（无需 Prefab 时仍享受统一生命周期与层管理）。</summary>
    protected TWindow OpenDynamicWindow<TWindow>(string windowKey, Transform parent = null)
        where TWindow : ModuleWindowBase
    {
        if (windowSlotByPath.TryGetValue(windowKey, out var existed) && existed.Window is TWindow existingWin)
        {
            existingWin.InternalOpen();
            return existingWin;
        }

        var go = new GameObject(windowKey, typeof(RectTransform));
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }

        var window = go.AddComponent<TWindow>();
        var slot = new WindowSlot
        {
            PrefabPath = windowKey,
            WindowType = typeof(TWindow),
            State = ModuleWindowState.Closed,
        };
        windowSlotByPath[windowKey] = slot;
        AttachWindowSlot(slot, window);
        return window;
    }

    private void AttachWindowSlot(WindowSlot slot, ModuleWindowBase window)
    {
        slot.Window = window;
        windowSlotByInstance[window] = slot;
        window.BindOwner(this, slot.PrefabPath, OnWindowStateChanged);
        window.InternalInit();
        window.InternalOpen();
    }

    private void HandleOpenWindowFailed(string prefabPath, WindowSlot slot)
    {
        ReleaseSlotHandle(slot);
        InvokeWindowCallbacks(slot, null);
        windowSlotByPath.Remove(prefabPath);
        TryDestroyWhenAllWindowsClosed();
    }

    private static void ApplyWindowRootStretch(GameObject windowGo)
    {
        var rectTransform = windowGo.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    protected void CloseWindow(string prefabPath)
    {
        if (!string.IsNullOrEmpty(prefabPath)
            && windowSlotByPath.TryGetValue(prefabPath, out var slot)
            && slot.Window != null)
        {
            slot.Window.CloseWindow();
        }
    }

    protected void HideWindow(string prefabPath)
    {
        if (!string.IsNullOrEmpty(prefabPath)
            && windowSlotByPath.TryGetValue(prefabPath, out var slot)
            && slot.Window != null)
        {
            slot.Window.HideWindow();
        }
    }

    private void OnWindowStateChanged(ModuleWindowBase window, ModuleWindowState state)
    {
        if (window == null || !windowSlotByInstance.TryGetValue(window, out var slot))
        {
            return;
        }

        slot.State = state;
        if (state != ModuleWindowState.Closed)
        {
            return;
        }

        windowSlotByInstance.Remove(window);
        if (slot.Window == window)
        {
            slot.Window = null;
        }

        ReleaseSlotHandle(slot);
        if (!slot.IsLoading)
        {
            windowSlotByPath.Remove(slot.PrefabPath);
        }

        TryDestroyWhenAllWindowsClosed();
    }

    private void TryDestroyWhenAllWindowsClosed()
    {
        if (!DestroyModuleWhenAllWindowsClosed || isDestroying)
        {
            return;
        }

        foreach (var slot in windowSlotByPath.Values)
        {
            if (slot.IsLoading || slot.State != ModuleWindowState.Closed)
            {
                return;
            }
        }

        ModuleMgr.Instance.DestroyModule(ModuleName);
    }

    private static void InvokeWindowCallbacks(WindowSlot slot, ModuleWindowBase window)
    {
        for (var i = 0; i < slot.PendingCallbacks.Count; i++)
        {
            slot.PendingCallbacks[i]?.Invoke(window);
        }

        slot.PendingCallbacks.Clear();
    }

    private void ReleaseSlotHandle(WindowSlot slot)
    {
        if (string.IsNullOrEmpty(slot.AssetPath))
        {
            return;
        }

        MVC.ResourceService.Release(slot.AssetPath, ModuleResourcePackage);
        slot.AssetPath = null;
    }
}

/// <summary>带强类型打开参数的模块基类。</summary>
public abstract class ModuleBase<TArgs> : ModuleBase
{
    protected sealed override async Task OpenAsync(object args, Action onOpened)
    {
        if (!TryResolveArgs(args, out var typedArgs))
        {
            onOpened?.Invoke();
            return;
        }

        await OnOpenAsync(typedArgs);
        onOpened?.Invoke();
    }

    protected virtual Task OnOpenAsync(TArgs args)
    {
        return Task.CompletedTask;
    }

    protected virtual bool AllowOpenWithNullArgs => true;
    protected virtual TArgs GetDefaultArgs() => default;

    private bool TryResolveArgs(object args, out TArgs typedArgs)
    {
        if (args == null)
        {
            if (!AllowOpenWithNullArgs)
            {
                GameDebug.LogError(ModuleName, $"打开失败：参数不能为空，期望={typeof(TArgs).Name}");
                typedArgs = default;
                return false;
            }

            typedArgs = GetDefaultArgs();
            return true;
        }

        if (args is TArgs casted)
        {
            typedArgs = casted;
            return true;
        }

        GameDebug.LogError(ModuleName, $"打开失败：参数类型不匹配，期望={typeof(TArgs).Name}，实际={args.GetType().Name}");
        typedArgs = default;
        return false;
    }
}
