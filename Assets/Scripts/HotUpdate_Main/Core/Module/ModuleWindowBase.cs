using System;
using UnityEngine;

public enum ModuleWindowState
{
    Opened = 0,
    Hidden = 1,
    Closed = 2,
}

/// <summary>
/// 模块 UGUI 窗口宿主基类（对齐 Demo ModuleWindowBase 生命周期：OnInit → OnOpen → OnHide → OnClose → OnDestroy）。
/// 底层使用 UGUI RectTransform + UiSortingLayer 挂载。
/// </summary>
public abstract class ModuleWindowBase : MonoBehaviour
{
    private Action<ModuleWindowBase, ModuleWindowState> stateChanged;
    private bool isClosed;
    private bool isOpened;
    private bool isInited;
    private bool layerRegistered;

    public string WindowPath { get; private set; }
    public ModuleBase OwnerModule { get; private set; }
    public RectTransform Root => transform as RectTransform;
    public bool IsOpened => isOpened;
    public bool IsClosed => isClosed;

    protected virtual UiSortingLayer SortingLayer => UiSortingLayer.Module;
    protected virtual bool FullScreen => true;
    public UiSortingLayer WindowSortingLayer => SortingLayer;

    internal void BindOwner(ModuleBase ownerModule, string windowPath, Action<ModuleWindowBase, ModuleWindowState> onStateChanged)
    {
        OwnerModule = ownerModule;
        WindowPath = windowPath;
        stateChanged = onStateChanged;
    }

    internal void InternalInit()
    {
        if (isInited)
        {
            return;
        }

        isInited = true;
        ApplySortingLayer();
        OnInit();
    }

    internal void InternalOpen()
    {
        if (isOpened)
        {
            BringToFrontInParent();
            return;
        }

        isOpened = true;
        isClosed = false;
        gameObject.SetActive(true);
        ApplySortingLayer();
        BringToFrontInParent();
        OnOpen();
        NotifyStateChanged(ModuleWindowState.Opened);
        NotifyOpenWindow();
    }

    public void HideWindow()
    {
        if (isClosed || !isOpened)
        {
            return;
        }

        isOpened = false;
        NotifyCloseWindow();
        OnHide();
        gameObject.SetActive(false);
        NotifyStateChanged(ModuleWindowState.Hidden);
    }

    public void CloseWindow()
    {
        if (isClosed)
        {
            return;
        }

        isOpened = false;
        NotifyCloseWindow();
        OnClose();
    }

    protected void CompleteClose()
    {
        if (isClosed)
        {
            return;
        }

        isClosed = true;
        NotifyStateChanged(ModuleWindowState.Closed);
    }

    protected virtual void OnInit() { }
    protected virtual void OnOpen() { }
    protected virtual void OnHide() { }

    protected virtual void OnClose()
    {
        Destroy(gameObject);
    }

    protected virtual void OnDestroy()
    {
        NotifyCloseWindow();
        CompleteClose();
    }

    protected void ApplySortingLayer()
    {
        if (Root != null)
        {
            UiMgr.Instance.MountWindow(this, SortingLayer, FullScreen);
        }
    }

    protected void BringToFrontInParent()
    {
        if (transform != null && transform.parent != null)
        {
            transform.SetAsLastSibling();
        }
    }

    private void NotifyOpenWindow()
    {
        var layer = SortingLayer;
        UiMgr.Instance.RegisterOpenedWindow(this, layer);
        layerRegistered = true;
        MVC.SendNotification(MainNoti.ON_OPEN_WINDOW, layer, GetType().Name);
    }

    private void NotifyCloseWindow()
    {
        if (!layerRegistered)
        {
            return;
        }

        layerRegistered = false;
        UiMgr.Instance.UnregisterOpenedWindow(this);
        MVC.SendNotification(MainNoti.ON_CLOSE_WINDOW, SortingLayer);
    }

    private void NotifyStateChanged(ModuleWindowState state)
    {
        stateChanged?.Invoke(this, state);
    }
}
