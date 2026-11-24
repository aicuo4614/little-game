using System;
using System.Collections.Generic;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 高于所有 UI 渲染层级的 3D 拖拽与抛物线回弹层：
    /// 1. DragOverlayCamera 与 CombatCamera 共享完全相同的正交尺寸与 39° 斜侧俯视旋转，
    ///    确保从候补 UI 拖出来的 3D 角色与战场内角色的像素大小、视角、战斗朝向（面向右 +X）100% 1:1 一致；
    /// 2. 满员拖入白框空处先轻踩停顿 0.13s 再沿抛物线飞回原候补格；拖出白框外同样抛物线弹回。
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
        private Vector3 ghostBaseScale = Vector3.one;
        private Vector2 lastScreenPos;
        private readonly List<BounceAnim> bounces = new List<BounceAnim>();

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
            dragCamera.allowHDR = true;

            var urpDrag = dragCamera.GetUniversalAdditionalCameraData();
            urpDrag.renderType = CameraRenderType.Overlay;
            urpDrag.renderPostProcessing = false;

            var baseData = mainCam.GetUniversalAdditionalCameraData();
            if (hudCamera != null && !baseData.cameraStack.Contains(hudCamera))
                baseData.cameraStack.Add(hudCamera);
            if (!baseData.cameraStack.Contains(dragCamera))
                baseData.cameraStack.Add(dragCamera);
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
            SyncCamera();
            activeGhost = CreateDragModel(troop);
            ghostBaseScale = activeGhost.transform.localScale;
            lastScreenPos = screenPos;
            PositionGhost(activeGhost, screenPos, 1.0f, 0f);
        }

        public void UpdateDrag(Vector2 screenPos)
        {
            if (activeGhost == null) return;
            SyncCamera();
            float dx = Mathf.Clamp((screenPos.x - lastScreenPos.x) * 0.25f, -8f, 8f);
            lastScreenPos = screenPos;
            PositionGhost(activeGhost, screenPos, 1.0f, -dx);
        }

        public void EndDragSuccess() => ClearActiveGhost();
        public void CancelDrag() => ClearActiveGhost();

        public void BounceBackToSlot(Troop troop, Vector2 landedScreenPos, Vector2 homeSlotScreenPos, bool pauseOnLand)
        {
            SyncCamera();
            GameObject go = activeGhost;
            activeGhost = null;
            if (go == null && troop != null)
            {
                go = CreateDragModel(troop);
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
            // 正交相机下任意固定深度(20m)投影与主战场像素大小 1:1 严格一致
            Vector3 world = dragCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 20f));
            go.transform.position = world;
            go.transform.localScale = ghostBaseScale * scaleMult;
            // 严格保持战斗朝向：面向右(+X, Yaw = 90°)，与战场中英雄方向完全一致
            go.transform.rotation = Quaternion.Euler(swayRoll, 90f, 0f);
        }

        private GameObject CreateDragModel(Troop troop)
        {
            string role = new[] { "Shield", "Archer", "Storm", "Frost" }[Mathf.Clamp((int)troop.Kind, 0, 3)];
            var appearance = Resources.Load<UnitAppearance>("UnitAppearances/" + role);

            var root = new GameObject("DragGhost_" + role);
            root.transform.SetParent(transform, false);

            // 与 UnitModelControl + CombatUnitRegistry 保持 1:1 高度和星级缩放
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
                }
                float scale = logicalHeight / Mathf.Max(0.01f, bounds.size.y);
                model.transform.localScale *= scale;
                model.transform.localPosition = new Vector3(0f, -bounds.min.y * scale, 0f);

                var anim = model.GetComponent<Animation>() ?? model.AddComponent<Animation>();
                if (appearance.Move != null)
                {
                    anim.AddClip(appearance.Move, "Walk");
                    anim["Walk"].wrapMode = WrapMode.Loop;
                    anim.Play("Walk");
                }
                else if (appearance.Idle != null)
                {
                    anim.AddClip(appearance.Idle, "Idle");
                    anim["Idle"].wrapMode = WrapMode.Loop;
                    anim.Play("Idle");
                }
            }
            else
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                model.transform.SetParent(root.transform, false);
                var col = model.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            SetLayerRecursively(root, DragLayer);
            return root;
        }

        private void ClearActiveGhost()
        {
            if (activeGhost != null)
            {
                Destroy(activeGhost);
                activeGhost = null;
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
            for (int i = bounces.Count - 1; i >= 0; i--)
                if (bounces[i].Root != null) Destroy(bounces[i].Root);
            bounces.Clear();
        }
    }
}
