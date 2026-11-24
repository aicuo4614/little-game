using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Client.Battle;
using LittleGame.Client.SkillTimeline;
using LittleGame.Client.Units;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LittleGame.Client
{
    /// <summary>
    /// 战斗三维场景视图协调器（横向左→右 39° 斜侧俯视 + 三层全屏 URP Camera Stack）：
    /// 镜头运动参考 E:\Project\bump 的 MainCameraMgr / CameraMgr / MapControl 设计：
    /// 战中平滑跟随前锋交战中心（带边界限幅与阻尼），战后缓动回正并按队伍实际平均移速匹配大道滚动与远景视差。
    /// </summary>
    public sealed class BattleSceneView : MonoBehaviour, ISkillTimelineActorHost
    {
        private const float BaseCamX = 0.25f;
        private const float CamBoundMinX = -0.15f;
        private const float CamBoundMaxX = 2.05f;

        private BattleApplication app;
        private CourtyardEnvironment environment;
        private CombatUnitRegistry unitRegistry;
        private SkillTimelineDirector timelineDirector;
        private BattleFieldOverlayView fieldOverlay;
        private BattleFloatingText floatingText;
        private BattleActionRouter actionRouter;
        private BattleDragOverlay dragOverlay;
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private Mesh ringMesh;
        private Transform world;
        private Light combatSun;

        private readonly Vector3 cameraBasePosition = new Vector3(BaseCamX, 14.0f, -17.6f);
        private readonly Vector3 cameraLookTarget = new Vector3(BaseCamX, 0.0f, -0.25f);
        private float camCurrentX = BaseCamX;
        private RunPhase lastPhase = RunPhase.Preparation;

        public Camera Camera { get; private set; }
        public Camera HudCamera { get; private set; }
        public BattleDragOverlay DragOverlay => dragOverlay;
        public BattleApplication App => app;
        public SkillTimelineDirector TimelineDirector => timelineDirector;

        public void Build(BattleApplication application)
        {
            app = application;
            world = new GameObject("BattleWorld").transform;
            world.SetParent(transform);

            foreach (var old in FindObjectsOfType<Camera>()) old.enabled = false;
            Camera = new GameObject("CombatCamera").AddComponent<Camera>();
            Camera.tag = "MainCamera";
            Camera.transform.SetParent(world);
            Camera.orthographic = true;
            Camera.orthographicSize = 11.25f;
            Camera.rect = new Rect(0, 0, 1, 1);
            Camera.cullingMask = ~(1 << 5) & ~(1 << BattleDragOverlay.DragLayer);
            Camera.transform.position = cameraBasePosition;
            Camera.transform.LookAt(cameraLookTarget);
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.095f, .095f, .155f);
            Camera.nearClipPlane = .1f;
            Camera.farClipPlane = 80f;
            Camera.allowHDR = true;

            var urpCam = Camera.GetUniversalAdditionalCameraData();
            urpCam.renderPostProcessing = true;
            SetupSelectiveEffectBloom(world);

            HudCamera = new GameObject("HudCamera").AddComponent<Camera>();
            HudCamera.transform.SetParent(world, false);
            HudCamera.orthographic = true;
            HudCamera.orthographicSize = 5f;
            HudCamera.rect = new Rect(0, 0, 1, 1);
            HudCamera.cullingMask = 1 << 5;
            HudCamera.nearClipPlane = 0.1f;
            HudCamera.farClipPlane = 50f;
            HudCamera.transform.position = new Vector3(0, 0, -10f);
            var urpHud = HudCamera.GetUniversalAdditionalCameraData();
            urpHud.renderType = CameraRenderType.Overlay;
            urpHud.renderPostProcessing = false;

            dragOverlay = new GameObject("BattleDragOverlay").AddComponent<BattleDragOverlay>();
            dragOverlay.transform.SetParent(world, false);
            dragOverlay.Initialize(Camera, HudCamera);

            foreach (var old in FindObjectsOfType<Light>()) old.enabled = false;
            combatSun = new GameObject("CombatSun").AddComponent<Light>();
            combatSun.transform.SetParent(world);
            combatSun.type = LightType.Directional;
            combatSun.transform.rotation = Quaternion.Euler(46, -28, 0);
            combatSun.shadows = LightShadows.Soft;
            combatSun.color = new Color(1f, .96f, .88f);
            combatSun.intensity = 1.18f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.65f, .71f, .82f);
            RenderSettings.ambientEquatorColor = new Color(.57f, .59f, .62f);
            RenderSettings.ambientGroundColor = new Color(.32f, .35f, .40f);

            environment = new GameObject("InfiniteRuinsRoad").AddComponent<CourtyardEnvironment>();
            environment.transform.SetParent(world, false);
            environment.Build();

            BuildRingMesh();
            unitRegistry = new CombatUnitRegistry(world, Camera, ringMesh, Mat, ColorFor);

            fieldOverlay = new GameObject("BattleFieldOverlay").AddComponent<BattleFieldOverlayView>();
            fieldOverlay.transform.SetParent(world, false);
            fieldOverlay.Initialize(combatSun, Camera, ringMesh);

            timelineDirector = new GameObject("SkillTimelineDirector").AddComponent<SkillTimelineDirector>();
            timelineDirector.transform.SetParent(world, false);
            timelineDirector.Initialize(this);

            floatingText = new GameObject("BattleFloatingText").AddComponent<BattleFloatingText>();
            floatingText.transform.SetParent(world, false);
            floatingText.Initialize(Camera);

            actionRouter = new BattleActionRouter(unitRegistry, timelineDirector, floatingText, GetLaneCenter);
        }

        public static Volume SetupSelectiveEffectBloom(Transform parent)
        {
            var go = new GameObject("SelectiveEffectBloomVolume");
            go.transform.SetParent(parent, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.08f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.62f);
            bloom.clamp.Override(4.0f);
            bloom.highQualityFiltering.Override(false);
            vol.sharedProfile = profile;
            return vol;
        }

        private void LateUpdate()
        {
            if (app == null) return;
            var s = app.Session;
            if (lastPhase == RunPhase.Fighting && s.Phase != RunPhase.Fighting)
            {
                timelineDirector?.ClearAll();
                floatingText?.ClearAll();
                fieldOverlay?.ResetDarkenImmediate();
            }
            lastPhase = s.Phase;

            float dt = Time.unscaledDeltaTime;
            bool paused = s.Paused || s.Aim != AimKind.None;

            // 参考 bump MainCameraMgr / CameraMgr：战中平滑跟随前锋与交战焦点，战后缓动回正到 BaseCamX
            float prevCamX = camCurrentX;
            if (!paused)
            {
                float targetCamX = BaseCamX;
                if (s.Phase == RunPhase.Fighting)
                {
                    var aliveTroops = s.Troops.Where(t => t.Deployed && t.Alive).ToArray();
                    var aliveEnemies = s.Enemies.Where(e => e.Alive).ToArray();
                    if (aliveTroops.Length > 0)
                    {
                        float vanguardX = aliveTroops.Max(t => t.X);
                        float focusX = aliveEnemies.Length > 0
                            ? Mathf.Lerp(vanguardX, aliveEnemies.Min(e => e.X), 0.38f)
                            : vanguardX;
                        // 偏移量 disX 保持我方阵型偏左、交战区偏中右，并做左右边界限幅
                        targetCamX = Mathf.Clamp(BaseCamX + (focusX + 1.15f) * 0.42f, CamBoundMinX, CamBoundMaxX);
                    }
                }
                float smoothRate = s.Phase == RunPhase.Fighting ? 3.2f : 4.5f;
                camCurrentX = Mathf.Lerp(camCurrentX, targetCamX, 1f - Mathf.Exp(-smoothRate * dt));
                Camera.transform.position = new Vector3(camCurrentX, cameraBasePosition.y, cameraBasePosition.z);
                dragOverlay?.SyncCamera();
            }
            float deltaCamX = camCurrentX - prevCamX;

            // 行军速度自动匹配我方当前上阵军团的平均 MoveSpeed（确保脚底步频与大道滚动一致）
            float avgMoveSpeed = s.Troops.Any(t => t.Deployed)
                ? s.Troops.Where(t => t.Deployed).Average(t => t.MoveSpeed)
                : 2.5f;
            float scrollSpeed = paused || s.Phase == RunPhase.Lost || s.Phase == RunPhase.Won
                ? 0f
                : s.Phase == RunPhase.Preparation
                    ? (s.IsRegrouping ? avgMoveSpeed * 0.45f : avgMoveSpeed * 0.72f)
                    : (s.Phase == RunPhase.WaveResult || s.Phase == RunPhase.Reward)
                        ? (s.IsRegrouping ? avgMoveSpeed * 0.65f : avgMoveSpeed * 1.45f)
                        : 0f;

            environment.Apply(scrollSpeed, deltaCamX, camCurrentX, dt);
            bool marching = scrollSpeed > 0.1f;
            unitRegistry.Sync(s, marching);
            actionRouter.ProcessEvents(s);

            float scaledDt = dt * Math.Max(1, s.Speed);
            timelineDirector?.Step(scaledDt, paused);
            floatingText?.Step(scaledDt, paused);
            fieldOverlay?.Sync(s, unitRegistry, dt);
        }

        public void SetSelectedUnit(int unitId, bool visible) => fieldOverlay?.SyncSelectionCursor(unitId, visible, unitRegistry);
        public void SetDragZoneVisual(bool dragging, Vector3 groundPos, bool insideBox, bool canPlace) =>
            fieldOverlay?.SetDragZoneVisual(dragging, groundPos, insideBox, canPlace);

        public bool TryGetUnitPosition(int unitId, out Vector3 position)
        {
            if (unitRegistry != null && unitRegistry.TryGet(unitId, out var actor) && actor != null)
            {
                position = actor.transform.position;
                return true;
            }
            position = Vector3.zero;
            return false;
        }

        public CharacterMotion GetUnitMotion(int unitId) =>
            unitRegistry != null && unitRegistry.TryGet(unitId, out var actor) ? actor.Motion : null;

        public Vector3 GetLaneCenter(int lane) => new Vector3(-2.4f, 0.14f, BattleDefinitions.CellZ(Mathf.Clamp(lane, 0, 2)));
        public Camera GetCamera() => Camera;
        public Vector3 GetCameraBasePosition() => new Vector3(camCurrentX, cameraBasePosition.y, cameraBasePosition.z);
        public void SetSceneDarken(float strength) => fieldOverlay?.SetTargetSceneDarken(strength);

        public Vector2 UnitScreen(int id) => unitRegistry != null ? unitRegistry.UnitScreen(id) : new Vector2(-1000, -1000);
        public Vector2 WorldToScreen(float x, float z) => Camera.WorldToScreenPoint(new Vector3(x, 0.12f, z));
        public Vector2 CellScreen(int lane, int row) => WorldToScreen(BattleDefinitions.CellX(row), BattleDefinitions.CellZ(lane));
        public int HitUnit(Vector2 screen, bool enemy) => unitRegistry != null ? unitRegistry.HitUnit(app.Session, screen, enemy) : 0;

        public static bool IsInBattleViewport(Vector2 screen) =>
            screen.x >= 0f && screen.x <= Screen.width && screen.y >= Screen.height * 0.32f && screen.y <= Screen.height * 0.81f;

        /// <summary>将屏幕坐标投射到战场地面平面 (Y=0.12)，用于自由拖拽判断与白框边界校验。</summary>
        public bool ScreenToGround(Vector2 screen, out Vector3 worldPos, bool requireViewport = true)
        {
            worldPos = Vector3.zero;
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

        public static Color ColorFor(int kind) => kind == 0 ? new Color(.25f, .59f, .76f) : kind == 1 ? new Color(.28f, .64f, .4f) : kind == 2 ? new Color(.58f, .35f, .82f) : new Color(.32f, .77f, .87f);

        public object Snapshot() => new
        {
            cameraX = Math.Round(camCurrentX, 2),
            environment = environment.Snapshot(),
            skillTimeline = timelineDirector != null ? timelineDirector.Snapshot() : null,
            actors = unitRegistry != null ? unitRegistry.SnapshotActors() : Array.Empty<object>()
        };

        private Material Mat(string key, Color color, bool unlit = false)
        {
            if (materials.TryGetValue(key, out var m)) return m;
            m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            m.color = color;
            materials.Add(key, m);
            return m;
        }

        private void BuildRingMesh()
        {
            const int segments = 40;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                vertices[i * 2] = dir * .5f;
                vertices[i * 2 + 1] = dir * .44f;
                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2; triangles[t + 1] = i * 2 + 1; triangles[t + 2] = next * 2;
                triangles[t + 3] = next * 2; triangles[t + 4] = i * 2 + 1; triangles[t + 5] = next * 2 + 1;
            }
            ringMesh = new Mesh { name = "Unit ground ring", vertices = vertices, triangles = triangles };
            ringMesh.RecalculateNormals();
            ringMesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            foreach (var m in materials.Values) Destroy(m);
            if (ringMesh != null) Destroy(ringMesh);
        }
    }
}
