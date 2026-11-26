using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UGUI 窗口层级与常驻 UI 管理器（替代 Demo 的 FairyGUIUtil，沿用 UiSortingLayer 与 MainNoti 契约）。
/// </summary>
public sealed class UiMgr : MgrBase<UiMgr>
{
    private readonly List<(ModuleWindowBase Window, UiSortingLayer Layer)> openedWindows =
        new List<(ModuleWindowBase, UiSortingLayer)>();
    private readonly HashSet<string> loadingFlags = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<(ModuleWindowBase Window, UiSortingLayer Layer)> OpenedWindows => openedWindows;
    public bool HasAnyLoading => loadingFlags.Count > 0;

    public RectTransform GetLayerRoot(UiSortingLayer layer)
    {
        return AOTUiRoot.Instance.GetLayerRoot(layer);
    }

    public void MountWindow(ModuleWindowBase window, UiSortingLayer layer, bool fullScreen = true)
    {
        if (window == null)
        {
            return;
        }

        var rt = window.Root;
        if (rt != null)
        {
            AOTUiRoot.Instance.Mount(rt, layer, fullScreen);
        }
    }

    public void RegisterOpenedWindow(ModuleWindowBase window, UiSortingLayer layer)
    {
        if (window == null)
        {
            return;
        }

        for (var i = 0; i < openedWindows.Count; i++)
        {
            if (ReferenceEquals(openedWindows[i].Window, window))
            {
                openedWindows[i] = (window, layer);
                return;
            }
        }

        openedWindows.Add((window, layer));
    }

    public void UnregisterOpenedWindow(ModuleWindowBase window)
    {
        if (window == null)
        {
            return;
        }

        for (var i = openedWindows.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(openedWindows[i].Window, window))
            {
                openedWindows.RemoveAt(i);
            }
        }
    }

    public void ShowLoading(string flag, string tip = null)
    {
        if (string.IsNullOrEmpty(flag))
        {
            return;
        }

        loadingFlags.Add(flag);
    }

    public void CloseLoading(string flag)
    {
        if (string.IsNullOrEmpty(flag))
        {
            return;
        }

        loadingFlags.Remove(flag);
    }

    public override void Clear()
    {
        openedWindows.Clear();
        loadingFlags.Clear();
        base.Clear();
    }
}
