using UnityEngine;

/// <summary>
/// 模块内 UGUI 面板基类（页内子区域 / 可复用组件，对齐 Demo ModulePanelBase）。
/// 生命周期：OnInit → OnOpen → OnClose（以及独立 OnShow / OnHide）。
/// </summary>
public abstract class ModulePanelBase
{
    private bool isInited;
    private bool isOpened;

    public ModuleWindowBase OwnerWindow { get; private set; }
    public RectTransform Root { get; private set; }
    public bool IsOpened => isOpened;
    public bool IsInited => isInited;

    public void InternalInit(ModuleWindowBase ownerWindow, RectTransform root)
    {
        if (isInited)
        {
            return;
        }

        OwnerWindow = ownerWindow;
        Root = root;
        isInited = true;
        OnInit();
    }

    public void InternalOpen()
    {
        if (!isInited)
        {
            return;
        }

        isOpened = true;
        if (Root != null)
        {
            Root.gameObject.SetActive(true);
        }

        OnOpen();
    }

    public void InternalClose()
    {
        if (!isOpened)
        {
            return;
        }

        OnClose();
        isOpened = false;
        if (Root != null)
        {
            Root.gameObject.SetActive(false);
        }
    }

    public void InternalShow()
    {
        if (!isInited)
        {
            return;
        }

        if (Root != null)
        {
            Root.gameObject.SetActive(true);
        }

        OnShow();
    }

    public void InternalHide()
    {
        if (!isInited)
        {
            return;
        }

        OnHide();
        if (Root != null)
        {
            Root.gameObject.SetActive(false);
        }
    }

    protected virtual void OnInit() { }
    protected virtual void OnOpen() { }
    protected virtual void OnClose() { }
    protected virtual void OnShow() { }
    protected virtual void OnHide() { }
}
