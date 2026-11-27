using System;
using System.Collections.Generic;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 高于所有 UI 渲染层级的 3D 拖拽、抛物线回弹与落点半透明剪影预览层：
    /// 1. DragOverlayCamera 与 CombatCamera 共享完全相同的正交尺寸与斜侧俯视旋转，
    ///    确保拖出来的 3D 角色与战场内角色大小、视角、战斗朝向（面向右 +X）100% 1:1 一致；
    /// 2. 拖拽未松手时，在可放置区域离手最近的点 A 显示半透明/剪影 3D 预览角色；
    /// 3. 满员拖入白框空处先轻踩停顿 0.13s 再沿抛物线飞回原候补格。
    /// </summary>
    public sealed class BattleDragOverlay : MonoBehaviour
    {
        public const int DragLayer = 31;

        private sealed class BounceAnim
        {
            public GameObject Root;
            public Vector2 StartScreen;
            public Vector2 EndScreen;
            public float PauseRemaining;
            public float Elapsed;
            public float Duration;
            public Vector3 BaseScale;
        }

        private Camera baseCamera;
        private Camera dragCamera;
        private GameObject activeGhost;
        private GameObject landingPreview;
        private Material silhouetteMat;
        private Vector3 ghostBaseScale = Vector3.one;
        private Vector2 lastScreenPos;
        private readonly List<BounceAnim> bounces = new List<BounceAnim>();
        private readonly Dictionary<string, UnitAppearance> appearances = new Dictionary<string, UnitAppearance>();

        public Camera DragCamera => dragCamera;
        public bool IsDragging => activeGhost != null;

        public void Initialize(Camera mainCam, Camera hudCamera)
        {
            baseCamera = mainCam;
            var camGo = new GameObject("DragOverlayCamera");
            camGo.transform.SetParent(transform, false);
            dragCamera = camGo.AddComponent<Camera>();
            dragCamera.orthographic = true;
            dragCamera.orthographicSize = mainCam.orthographicSize;
            dragCamera.rect = new Rect(0f, 0f, 1f, 1f);
            dragCamera.cullingMask = 1 << DragLayer;
            dragCamera.nearClipPlane = 0.1f;
            dragCamera.farClipPlane = 80f;
            dragCamera.transform.position = mainCam.transform.position;
            dragCamera.transform.rotation = mainCam.transform.rotation;
            dragCamera.allowHDR = false;
            dragCamera.enabled = false;

            var urpDrag = dragCamera.GetUniversalAdditionalCameraData();
            urpDrag.renderType = CameraRenderType.Overlay;
            urpDrag.renderPostProcessing = false;

            var baseData = mainCam.GetUniversalAdditionalCameraData();
            if (hudCamera != null && !baseData.cameraStack.Contains(hudCamera))
                baseData.cameraStack.Add(hudCamera);
            if (!baseData.cameraStack.Contains(dragCamera))
                baseData.cameraStack.Add(dragCamera);

            BuildSilhouetteMaterial();
        }

        private void BuildSilhouetteMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            silhouetteMat = new Material(shader);
            var col = new Color(0.72f, 0.92f, 1.0f, 0.44f);
            silhouetteMat.color = col;
            if (silhouetteMat.HasProperty("_BaseColor")) silhouetteMat.SetColor("_BaseColor", col);
            silhouetteMat.SetFloat("_Surface", 1f);
            silhouetteMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            silhouetteMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            silhouetteMat.SetFloat("_ZWrite", 0f);
            silhouetteMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            silhouetteMat.SetOverrideTag("RenderType", "Transparent");
            silhouetteMat.renderQueue = (int)RenderQueue.Transparent + 10;
        }

        public void SyncCamera()
        {
            if (baseCamera == null || dragCamera == null) return;
            dragCamera.orthographicSize = baseCamera.orthographicSize;
            dragCamera.transform.position = baseCamera.transform.position;
            dragCamera.transform.rotation = baseCamera.transform.rotation;
        }

        public void BeginDrag(Troop troop, Vector2 screenPos)
        {
            ClearActiveGhost();
            if (troop == null || dragCamera == null) return;
            dragCamera.enabled = true;
            SyncCamera();
            activeGhost = CreateDragModel(troop, DragLayer, asSilhouette: false);
            ghostBaseScale = activeGhost.transform.localScale;
            lastScreenPos = screenPos;
            PositionGhost(activeGhost, screenPos, 1.0f, 0f);

            // 创建主战场图层(Layer 0)上的落点半透明剪影角色预览
            landingPreview = CreateDragModel(troop, 0, asSilhouette: true);
            landingPreview.name = "DragLandingSilhouette";
            landingPreview.SetActive(false);
        }

        public void UpdateDrag(Vector2 screenPos)
        {
            if (activeGhost == null) return;
            SyncCamera();
            float dx = Mathf.Clamp((screenPos.x - lastScreenPos.x) * 0.25f, -8f, 8f);
            lastScreenPos = screenPos;
            PositionGhost(activeGhost, screenPos, 1.0f, -dx);
        }

        /// <summary>更新可放置区域最近点 A 的半透明剪影角色位置与颜色。</summary>
        public void UpdateLandingPreview(bool visible, Vector3 clampedWorldPos, bool isReplace)
        {
            if (landingPreview == null) return;
            landingPreview.SetActive(visible);
            if (!visible) return;
            landingPreview.transform.position = new Vector3(clampedWorldPos.x, 0.11f, clampedWorldPos.z);
            landingPreview.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            if (silhouetteMat != null)
            {
                Color c = isReplace
                    ? new Color(1.0f, 0.88f, 0.42f, 0.48f)
                    : new Color(0.68f, 0.92f, 1.0f, 0.44f);
                silhouetteMat.color = c;
                if (silhouetteMat.HasProperty("_BaseColor")) silhouetteMat.SetColor("_BaseColor", c);
            }
        }

        public void EndDragSuccess() => ClearActiveGhost();
        public void CancelDrag() => ClearActiveGhost();

        public void BounceBackToSlot(Troop troop, Vector2 landedScreenPos, Vector2 homeSlotScreenPos, bool pauseOnLand)
        {
            SyncCamera();
            if (landingPreview != null)
            {
                Destroy(landingPreview);
                landingPreview = null;
            }
            GameObject go = activeGhost;
            activeGhost = null;
            if (go == null && troop != null)
            {
                go = CreateDragModel(troop, DragLayer, asSilhouette: false);
                ghostBaseScale = go.transform.localScale;
            }
            if (go == null) return;

            PositionGhost(go, landedScreenPos, 1.0f, 0f);
            bounces.Add(new BounceAnim
            {
                Root = go,
                StartScreen = landedScreenPos,
                EndScreen = homeSlotScreenPos,
                PauseRemaining = pauseOnLand ? 0.13f : 0f,
                Elapsed = 0f,
                Duration = 0.36f,
                BaseScale = ghostBaseScale
            });
        }

        private void Update()
        {
            bool needOverlay = activeGhost != null || bounces.Count > 0;
            if (dragCamera != null && dragCamera.enabled != needOverlay)
                dragCamera.enabled = needOverlay;
            if (!needOverlay) return;
            SyncCamera();
            float dt = Time.unscaledDeltaTime;
            for (int i = bounces.Count - 1; i >= 0; i--)
            {
                var b = bounces[i];
                if (b.Root == null) { bounces.RemoveAt(i); continue; }

                if (b.PauseRemaining > 0f)
                {
                    b.PauseRemaining -= dt;
                    float k = Mathf.Sin(Mathf.Clamp01(b.PauseRemaining / 0.13f) * Mathf.PI);
                    b.Root.transform.localScale = new Vector3(
                        b.BaseScale.x * (1f + 0.14f * k),
                        b.BaseScale.y * (1f - 0.16f * k),
                        b.BaseScale.z * (1f + 0.14f * k));
                    continue;
                }

                b.Elapsed += dt;
                float t = Mathf.Clamp01(b.Elapsed / b.Duration);
                if (t >= 1f)
                {
                    Destroy(b.Root);
                    bounces.RemoveAt(i);
                    continue;
                }

                Vector2 linear = Vector2.Lerp(b.StartScreen, b.EndScreen, t);
                float arcPx = Mathf.Sin(t * Mathf.PI) * Mathf.Max(95f, Screen.height * 0.14f);
                Vector2 curScreen = linear + new Vector2(0f, arcPx);
                float scaleFactor = Mathf.Lerp(1.0f, 0.58f, t);
                PositionGhost(b.Root, curScreen, scaleFactor, Mathf.Sin(t * Mathf.PI * 2f) * 8f);
            }
        }

        private void PositionGhost(GameObject go, Vector2 screenPos, float scaleMult, float swayRoll)
        {
            if (go == null || dragCamera == null) return;
            Vector3 world = dragCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 20f));
            go.transform.position = world;
            go.transform.localScale = ghostBaseScale * scaleMult;
            go.transform.rotation = Quaternion.Euler(swayRoll, 90f, 0f);
        }

        private GameObject CreateDragModel(Troop troop, int layer, bool asSilhouette)
        {
            string role = troop.Definition.Role;
            if (!appearances.TryGetValue(role, out var appearance))
            {
                appearance = UnitAppearance.Load(role);
                appearances.Add(role, appearance);
            }

            var root = new GameObject("DragGhost_" + role);
            root.transform.SetParent(transform, false);

            float logicalHeight = appearance != null ? appearance.Height : 0.92f;
            float starScale = troop.Star == 3 ? 1.18f : troop.Star == 2 ? 1.08f : 1f;
            root.transform.localScale = Vector3.one * starScale;

            GameObject model;
            if (appearance != null && appearance.Model != null)
            {
                model = Instantiate(appearance.Model, root.transform);
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
                foreach (var r in renderers)
                {
                    bounds.Encapsulate(r.bounds);
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    if (asSilhouette && silhouetteMat != null)
                    {
                        var mats = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = silhouetteMat;
                        r.sharedMaterials = mats;
                    }
                }
                float scale = logicalHeight / Mathf.Max(0.01f, bounds.size.y);
                model.transform.localScale *= scale;
                model.transform.localPosition = new Vector3(0f, -bounds.min.y * scale, 0f);

                var animator = model.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    animator.runtimeAnimatorController = appearance.AnimatorController;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Play(asSilhouette ? "Idle" : "Walk", 0, 0f);
                }
            }
            else
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                model.transform.SetParent(root.transform, false);
                var col = model.GetComponent<Collider>();
                if (col != null) Destroy(col);
                if (asSilhouette && silhouetteMat != null)
                {
                    var r = model.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = silhouetteMat;
                }
            }

            SetLayerRecursively(root, layer);
            return root;
        }

        private void ClearActiveGhost()
        {
            if (activeGhost != null)
            {
                Destroy(activeGhost);
                activeGhost = null;
            }
            if (landingPreview != null)
            {
                Destroy(landingPreview);
                landingPreview = null;
            }
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private void OnDestroy()
        {
            ClearActiveGhost();
            if (silhouetteMat != null) Destroy(silhouetteMat);
            for (int i = bounces.Count - 1; i >= 0; i--)
                if (bounces[i].Root != null) Destroy(bounces[i].Root);
            bounces.Clear();
            foreach (var role in appearances.Keys) UnitAppearance.Release(role);
            appearances.Clear();
        }
    }
}
