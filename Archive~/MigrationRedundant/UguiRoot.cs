using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UGUI 分层根容器（替代 Demo 的 FairyGUI GRoot）：
/// 按 <see cref="UiSortingLayer"/> 维护独立排序层级，供 AOT 与热更层 ModuleWindowBase / StandalonePanelBase 共用。
/// </summary>
public sealed class UguiRoot : MonoBehaviour
{
    private static UguiRoot instance;
    private readonly Dictionary<UiSortingLayer, RectTransform> layers = new Dictionary<UiSortingLayer, RectTransform>();
    private Canvas rootCanvas;
    private Camera uiCamera;

    public static UguiRoot Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("[UguiRoot]");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<UguiRoot>();
                instance.Initialize();
            }
            return instance;
        }
    }

    public Canvas RootCanvas => rootCanvas;

    private void Initialize()
    {
        rootCanvas = gameObject.AddComponent<Canvas>();
        rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        rootCanvas.sortingOrder = 0;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();
    }

    /// <summary>绑定 UGUI 渲染相机（如 URP Overlay HudCamera）；为空时回退 ScreenSpaceOverlay。</summary>
    public void BindCamera(Camera camera)
    {
        uiCamera = camera;
        if (rootCanvas == null) return;
        if (uiCamera != null)
        {
            rootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            rootCanvas.worldCamera = uiCamera;
            rootCanvas.planeDistance = 10f;
            gameObject.layer = 5;
        }
        else
        {
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
    }

    /// <summary>获取或创建指定 <see cref="UiSortingLayer"/> 对应的层容器。</summary>
    public RectTransform GetLayer(UiSortingLayer layer)
    {
        if (layers.TryGetValue(layer, out var existing) && existing != null)
        {
            return existing;
        }

        var go = new GameObject("Layer_" + layer, typeof(RectTransform));
        go.layer = gameObject.layer;
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var layerCanvas = go.AddComponent<Canvas>();
        layerCanvas.overrideSorting = true;
        layerCanvas.sortingOrder = (int)layer;
        go.AddComponent<GraphicRaycaster>();

        layers[layer] = rect;
        return rect;
    }
}
