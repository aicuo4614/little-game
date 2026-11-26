using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// AOT 与热更层共用的 UGUI 分层 Canvas 根节点（对标 Demo 的 GRoot + UiSortingLayer）。
/// 按 <see cref="UiSortingLayer"/> 维护独立排序层，保证弹窗、Loading、提示和调试层顺序稳定。
/// </summary>
public sealed class AOTUiRoot : MonoBehaviour
{
    private static AOTUiRoot instance;
    private readonly Dictionary<UiSortingLayer, RectTransform> layerRoots = new Dictionary<UiSortingLayer, RectTransform>();
    private Canvas rootCanvas;

    public static AOTUiRoot Instance
    {
        get
        {
            if (instance == null)
            {
                EnsureCreated();
            }

            return instance;
        }
    }

    public Canvas RootCanvas => rootCanvas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    public static AOTUiRoot EnsureCreated()
    {
        if (instance != null)
        {
            return instance;
        }

        var go = new GameObject("[UiRoot]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AOTUiRoot>();
        instance.InitCanvas();
        return instance;
    }

    private void InitCanvas()
    {
        gameObject.layer = 5;
        rootCanvas = gameObject.AddComponent<Canvas>();
        rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        rootCanvas.sortingOrder = 0;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(540f, 960f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;

        gameObject.AddComponent<GraphicRaycaster>();
    }

    /// <summary>将根 Canvas 绑定到指定 Overlay 相机（可选）。</summary>
    public void BindCamera(Camera uiCamera)
    {
        if (rootCanvas == null)
        {
            return;
        }

        if (uiCamera != null)
        {
            rootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            rootCanvas.worldCamera = uiCamera;
            rootCanvas.planeDistance = 5f;
        }
        else
        {
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.worldCamera = null;
        }
    }

    /// <summary>获取或创建指定 <see cref="UiSortingLayer"/> 对应的 UGUI 容器节点。</summary>
    public RectTransform GetLayerRoot(UiSortingLayer layer)
    {
        if (layerRoots.TryGetValue(layer, out var existing) && existing != null)
        {
            return existing;
        }

        var go = new GameObject("Layer_" + layer);
        go.layer = 5;
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var canvas = go.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = (int)layer;
        go.AddComponent<GraphicRaycaster>();

        layerRoots[layer] = rt;
        return rt;
    }

    /// <summary>将任意 UGUI 窗口节点挂到对应排序层并铺满。</summary>
    public void Mount(RectTransform child, UiSortingLayer layer, bool fullScreen = true)
    {
        if (child == null)
        {
            return;
        }

        var parent = GetLayerRoot(layer);
        child.SetParent(parent, false);
        if (fullScreen)
        {
            child.anchorMin = Vector2.zero;
            child.anchorMax = Vector2.one;
            child.offsetMin = Vector2.zero;
            child.offsetMax = Vector2.zero;
        }

        child.SetAsLastSibling();
    }
}

/// <summary>AOT 侧 UGUI 独立面板基类（对标 Demo AOTStandalonePanelBase）。</summary>
public abstract class AOTStandalonePanelBase : MonoBehaviour
{
    public RectTransform Root => transform as RectTransform;
    public virtual UiSortingLayer SortingLayer => UiSortingLayer.Loading;
    public bool IsOpened { get; private set; }

    public void Open()
    {
        AOTUiRoot.Instance.Mount(Root, SortingLayer, true);
        gameObject.SetActive(true);
        IsOpened = true;
        OnOpen();
    }

    public void Close()
    {
        if (!IsOpened)
        {
            return;
        }

        IsOpened = false;
        OnClose();
        gameObject.SetActive(false);
    }

    protected virtual void OnOpen() { }
    protected virtual void OnClose() { }
}
