using LittleGame.Combat;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 战斗相机、三层 URP Camera Stack 与拖拽覆盖层子系统（对齐 Demo BattleCameraControl）：
    /// 保持 CameraFollowRig -> CameraRotatePivot -> ShakeTrans -> CombatCamera 层级，
    /// 并绑定 BattleCameraMono 以原生承接共享 TimelinePlayback 的相机震动与运镜。
    /// </summary>
    public sealed class BattleCameraControl : BattleControlBase
    {
        private const float MaxForwardOffset = 1.4f;
        private const float TrackingDeadZone = 0.4f;
        private const float LeftTroopMargin = 0.72f;

        private readonly Vector3 cameraLookTarget = PortraitBattleCameraSettings.LookTarget;
        private readonly Vector3 cameraLocalOffset = PortraitBattleCameraSettings.Offset;

        private Transform cameraFollowRig;
        private Transform cameraRotatePivot;
        private Transform shakeTrans;
        private BattleCameraMono cameraMono;
        private BattleDragOverlay dragOverlay;
        private float camCurrentX;

        public Camera Camera { get; private set; }
        public Camera HudCamera { get; private set; }
        public BattleDragOverlay DragOverlay => dragOverlay;
        public BattleCameraMono CameraMono => cameraMono;
        public Transform ShakeTransform => shakeTrans;
        public Transform CameraRoot => cameraFollowRig;
        public float CameraX => camCurrentX;
        public float CameraYaw => Mathf.DeltaAngle(0f, cameraRotatePivot.localEulerAngles.y);
        public float BaseCameraX => cameraLookTarget.x;
        public float MaxCameraX => BaseCameraX + MaxForwardOffset;

        public override void Init(BattleView owner)
        {
            base.Init(owner);

            cameraFollowRig = new GameObject("CameraFollowRig").transform;
            cameraFollowRig.SetParent(owner.WorldRoot, false);
            cameraFollowRig.localPosition = cameraLookTarget;
            camCurrentX = BaseCameraX;

            cameraRotatePivot = new GameObject("CameraRotatePivot").transform;
            cameraRotatePivot.SetParent(cameraFollowRig, false);
            cameraRotatePivot.localPosition = Vector3.zero;
            cameraRotatePivot.localRotation = Quaternion.identity;

            shakeTrans = new GameObject("ShakeTrans").transform;
            shakeTrans.SetParent(cameraRotatePivot, false);
            shakeTrans.localPosition = Vector3.zero;
            shakeTrans.localRotation = Quaternion.identity;

            foreach (var old in Object.FindObjectsOfType<Camera>()) old.enabled = false;
            Camera = new GameObject("CombatCamera").AddComponent<Camera>();
            Camera.tag = "MainCamera";
            Camera.transform.SetParent(shakeTrans, false);
            Camera.orthographic = true;
            Camera.orthographicSize = PortraitBattleCameraSettings.OrthographicSize;
            Camera.rect = new Rect(0f, 0f, 1f, 1f);
            Camera.cullingMask = ~(1 << 5) & ~(1 << BattleDragOverlay.DragLayer);
            Camera.transform.localPosition = cameraLocalOffset;
            Camera.transform.LookAt(cameraRotatePivot.position);
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = BattleView.BattleBackgroundColor;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 80f;

            bool isMobile = Application.isMobilePlatform;
            Camera.allowHDR = !isMobile;
            var urpCam = Camera.GetUniversalAdditionalCameraData();
            urpCam.renderPostProcessing = !isMobile;

            cameraMono = cameraFollowRig.gameObject.AddComponent<BattleCameraMono>();
            cameraMono.RootTrans = cameraFollowRig;
            cameraMono.ShakeTrans = shakeTrans;
            cameraMono.ContentTrans = Camera.transform;
            cameraMono.MyCamera = Camera;

            HudCamera = new GameObject("HudCamera").AddComponent<Camera>();
            HudCamera.transform.SetParent(owner.WorldRoot, false);
            HudCamera.orthographic = true;
            HudCamera.orthographicSize = 5f;
            HudCamera.rect = new Rect(0f, 0f, 1f, 1f);
            HudCamera.cullingMask = 1 << 5;
            HudCamera.nearClipPlane = 0.1f;
            HudCamera.farClipPlane = 50f;
            HudCamera.transform.position = new Vector3(0f, 0f, -10f);
            var urpHud = HudCamera.GetUniversalAdditionalCameraData();
            urpHud.renderType = CameraRenderType.Overlay;
            urpHud.renderPostProcessing = false;

            dragOverlay = new GameObject("BattleDragOverlay").AddComponent<BattleDragOverlay>();
            dragOverlay.transform.SetParent(owner.WorldRoot, false);
            dragOverlay.Initialize(Camera, HudCamera);
        }

        public float UpdateCamera(BattleSession session, bool paused, float dt)
        {
            // 窄屏也保留 9:16 基础机位的横向可见宽度。
            float baseSize = PortraitBattleCameraSettings.OrthographicSize;
            Camera.orthographicSize = baseSize * Mathf.Max(1f, PortraitBattleCameraSettings.Aspect / Camera.aspect);
            float prevCamX = camCurrentX;
            if (!paused)
            {
                float targetCamX = BaseCameraX;
                if (session.Phase == RunPhase.Fighting)
                {
                    float vanguardX = float.MinValue;
                    float leftmostX = float.MaxValue;
                    var troops = session.Troops;
                    for (int i = 0; i < troops.Count; i++)
                    {
                        var troop = troops[i];
                        if (!troop.Deployed || !troop.Alive) continue;
                        vanguardX = Mathf.Max(vanguardX, troop.X);
                        leftmostX = Mathf.Min(leftmostX, troop.X);
                    }
                    if (vanguardX > float.MinValue)
                    {
                        float enemyMinX = float.MaxValue;
                        bool bossAlive = false;
                        var enemies = session.Enemies;
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            var enemy = enemies[i];
                            if (!enemy.Alive) continue;
                            enemyMinX = Mathf.Min(enemyMinX, enemy.X);
                            bossAlive |= enemy.Boss;
                        }
                        float focusX = enemyMinX < float.MaxValue ? Mathf.Lerp(vanguardX, enemyMinX, 0.38f) : vanguardX;
                        // 交战中心在当前构图附近时保持稳定，越出缓冲带才平移。
                        float delta = focusX - camCurrentX;
                        float track = Mathf.Sign(delta) * Mathf.Max(0f, Mathf.Abs(delta) - TrackingDeadZone);
                        // 右移时给落在后方的友军留出轮廓与血条空间。
                        float halfWidth = Camera.orthographicSize * Camera.aspect;
                        float visibleMaxX = leftmostX + halfWidth - LeftTroopMargin;
                        if (bossAlive)
                        {
                            float warningLeft = BattleFieldOverlayView.BossTelegraphCenterX - BattleFieldOverlayView.BossTelegraphLength * .5f;
                            visibleMaxX = Mathf.Min(visibleMaxX, warningLeft + halfWidth - .1f);
                        }
                        visibleMaxX = Mathf.Clamp(visibleMaxX, BaseCameraX, MaxCameraX);
                        targetCamX = Mathf.Clamp(camCurrentX + track, BaseCameraX, visibleMaxX);
                    }
                    else
                    {
                        targetCamX = camCurrentX;
                    }
                }

                float smoothRate = session.Phase == RunPhase.Fighting ? 2.8f : 4.5f;
                camCurrentX = Mathf.Lerp(camCurrentX, targetCamX, 1f - Mathf.Exp(-smoothRate * dt));
                if (Mathf.Abs(camCurrentX - targetCamX) < 0.001f) camCurrentX = targetCamX;
                if (cameraFollowRig != null)
                {
                    cameraFollowRig.localPosition = new Vector3(camCurrentX, cameraLookTarget.y, cameraLookTarget.z);
                }
            }

            dragOverlay?.SyncCamera();
            return camCurrentX - prevCamX;
        }

        public void ResetShake()
        {
            if (shakeTrans != null)
            {
                shakeTrans.localPosition = Vector3.zero;
                shakeTrans.localRotation = Quaternion.identity;
            }
        }

        public Vector2 WorldToScreen(float x, float z) =>
            Camera != null ? Camera.WorldToScreenPoint(new Vector3(x, 0.12f, z)) : Vector2.zero;

        public Vector2 CellScreen(int lane, int row) =>
            WorldToScreen(BattleDefinitions.CellX(row), BattleDefinitions.CellZ(lane));

        public static bool IsInBattleViewport(Vector2 screen) =>
            screen.x >= 0f && screen.x <= Screen.width && screen.y >= Screen.height * 0.32f && screen.y <= Screen.height * 0.81f;

        public bool ScreenToGround(Vector2 screen, out Vector3 worldPos, bool requireViewport = true)
        {
            worldPos = Vector3.zero;
            if (Camera == null) return false;
            if (requireViewport && !IsInBattleViewport(screen)) return false;
            var ray = Camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0f, 0.12f, 0f));
            if (!plane.Raycast(ray, out float enter)) return false;
            worldPos = ray.GetPoint(enter);
            return true;
        }

        public int HitLane(Vector2 screen)
        {
            if (!ScreenToGround(screen, out var pt, requireViewport: true)) return -1;
            if (Mathf.Abs(pt.z) > 3.6f) return -1;
            return BattleDefinitions.NearestLane(pt.z);
        }

        public override void ReleaseResources()
        {
            if (dragOverlay != null) Object.Destroy(dragOverlay.gameObject);
            if (HudCamera != null) Object.Destroy(HudCamera.gameObject);
            if (cameraFollowRig != null) Object.Destroy(cameraFollowRig.gameObject);
            dragOverlay = null;
            HudCamera = null;
            Camera = null;
            cameraFollowRig = null;
            cameraRotatePivot = null;
            shakeTrans = null;
            cameraMono = null;
        }
    }
}
