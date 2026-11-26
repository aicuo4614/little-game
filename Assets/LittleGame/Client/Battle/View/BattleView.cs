using System;
using System.Collections.Generic;
using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 战斗三维表现组合宿主（严格对齐 Demo BattleFullView / BattleView）：
    /// 持有 PoolGroup 与 9 个职责单一的 BattleControlBase 子系统，统一初始化、逐帧驱动、阶段清理与逆序释放。
    /// </summary>
    public sealed class BattleView : MonoBehaviour
    {
        public static readonly Color BattleBackgroundColor = new Color(0.145f, 0.165f, 0.225f, 1f);
        private readonly List<BattleControlBase> controls = new List<BattleControlBase>(12);
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private bool ready;
        private bool tornDown;
        private RunPhase lastPhase = RunPhase.Preparation;

        public BattleApplication App { get; private set; }
        public Transform WorldRoot { get; private set; }
        public PoolGroup PrefabPool { get; private set; }
        public Mesh RingMesh { get; private set; }
        public bool IsTornDown => tornDown;

        public BattleSceneControl SceneControl => GetControl<BattleSceneControl>();
        public BattleCameraControl CameraControl => GetControl<BattleCameraControl>();
        public BattleUnitsControl UnitsControl => GetControl<BattleUnitsControl>();
        public BattleEffectControl EffectControl => GetControl<BattleEffectControl>();
        public BattleActionControl ActionControl => GetControl<BattleActionControl>();
        public BattleFieldOverlayControl FieldOverlayControl => GetControl<BattleFieldOverlayControl>();
        public BattleSpeedControl SpeedControl => GetControl<BattleSpeedControl>();
        public BattleHudControl HudControl => GetControl<BattleHudControl>();
        public BattleInputControl InputControl => GetControl<BattleInputControl>();

        // 便捷透传属性，供 BattleHud / BattleInput / CombatMcp 统一读取
        public Camera Camera => CameraControl.Camera;
        public Camera HudCamera => CameraControl.HudCamera;
        public BattleDragOverlay DragOverlay => CameraControl.DragOverlay;

        public T GetControl<T>() where T : BattleControlBase
        {
            for (int i = 0; i < controls.Count; i++)
            {
                if (controls[i] is T match)
                {
                    return match;
                }
            }
            throw new InvalidOperationException("Battle control missing: " + typeof(T).Name);
        }

        public void Build(BattleApplication application)
        {
            App = application;
            WorldRoot = new GameObject("BattleWorld").transform;
            WorldRoot.SetParent(transform, false);

            PrefabPool = PoolMgr.Instance.CreateGroup(transform);
            BuildRingMesh();

            RegisterControl(new BattleSceneControl());
            RegisterControl(new BattleCameraControl());
            RegisterControl(new BattleUnitsControl());
            RegisterControl(new BattleEffectControl());
            RegisterControl(new BattleActionControl());
            RegisterControl(new BattleFieldOverlayControl());
            RegisterControl(new BattleSpeedControl());
            RegisterControl(new BattleHudControl());
            RegisterControl(new BattleInputControl());

            for (int i = 0; i < controls.Count; i++)
            {
                controls[i].Init(this);
            }
            for (int i = 0; i < controls.Count; i++)
            {
                controls[i].Start();
            }

            // 将共享 Demo TimelineConfig 推导出的精确时序注册进纯规则层
            App.Session.ConfigureSkills(ActionControl.Library.BuildDefinitions());
            ready = true;
        }

        private void RegisterControl(BattleControlBase control)
        {
            controls.Add(control);
        }

        private void LateUpdate()
        {
            if (!ready || tornDown || App == null) return;
            var session = App.Session;

            if (lastPhase == RunPhase.Fighting && session.Phase != RunPhase.Fighting)
            {
                ActionControl.ClearAll();
                EffectControl.ClearTransient();
                FieldOverlayControl.ResetDarkenImmediate();
            }
            lastPhase = session.Phase;

            float dt = Time.unscaledDeltaTime;
            bool paused = session.Paused || session.Aim != AimKind.None;

            SpeedControl.Sync(session.Speed);
            float deltaCamX = CameraControl.UpdateCamera(session, paused, dt);
            SceneControl.SyncEnvironment(session, paused, deltaCamX, CameraControl.CameraX, dt, out bool marching);
            UnitsControl.Sync(session, marching, dt);
            ActionControl.ProcessEvents(session);

            float scaledDt = dt * Math.Max(1, SpeedControl.CurrentSpeed);
            ActionControl.Step(scaledDt, paused);
            EffectControl.Step(scaledDt, paused);
            FieldOverlayControl.Sync(session, dt);

            for (int i = 0; i < controls.Count; i++)
            {
                controls[i].Tick(dt);
            }
        }

        public void SetSelectedUnit(int unitId, bool visible) => FieldOverlayControl.SyncSelectionCursor(unitId, visible);
        public void SetDragZoneVisual(bool dragging, Vector3 clampedPos, bool showSpot, bool isReplace) =>
            FieldOverlayControl.SetDragZoneVisual(dragging, clampedPos, showSpot, isReplace);

        public Vector3 GetLaneCenter(int lane) =>
            new Vector3(-2.4f, 0.14f, BattleDefinitions.CellZ(Mathf.Clamp(lane, 0, 2)));

        public Vector2 UnitScreen(int id) => UnitsControl.UnitScreen(id);
        public Vector2 WorldToScreen(float x, float z) => CameraControl.WorldToScreen(x, z);
        public Vector2 CellScreen(int lane, int row) => CameraControl.CellScreen(lane, row);
        public int HitUnit(Vector2 screen, bool enemy) => UnitsControl.HitUnit(App.Session, screen, enemy);
        public bool ScreenToGround(Vector2 screen, out Vector3 worldPos, bool requireViewport = true) =>
            CameraControl.ScreenToGround(screen, out worldPos, requireViewport);
        public int HitLane(Vector2 screen) => CameraControl.HitLane(screen);
        public static bool IsInBattleViewport(Vector2 screen) => BattleCameraControl.IsInBattleViewport(screen);

        public static Color ColorFor(int kind) =>
            kind == 0 ? new Color(0.25f, 0.59f, 0.76f)
            : kind == 1 ? new Color(0.28f, 0.64f, 0.4f)
            : kind == 2 ? new Color(0.58f, 0.35f, 0.82f)
            : new Color(0.32f, 0.77f, 0.87f);

        public Material GetSharedMaterial(string key, Color color, bool unlit = false)
        {
            if (materials.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            m.color = color;
            materials[key] = m;
            return m;
        }

        public object Snapshot() => new
        {
            framework = FrameworkDiagnostics.Snapshot(this),
            architecture = "Demo-Aligned:AOT->MainHotUpdate->MVC->ModuleMgr->BattleModule->BattleView(9 Controls)->BattleUnit(7 Controls+FSM)",
            cameraX = Math.Round(CameraControl.CameraX, 2),
            cameraYaw = Math.Round(CameraControl.CameraYaw, 2),
            cameraMode = "fixed-heading-horizontal-follow",
            cameraBaseX = CameraControl.BaseCameraX,
            cameraMaxX = CameraControl.MaxCameraX,
            cameraRig = "CameraFollowRig->CameraRotatePivot->ShakeTrans->CombatCamera",
            environment = SceneControl.Snapshot(),
            skillTimeline = ActionControl.Snapshot(),
            actors = UnitsControl.SnapshotActors(),
        };

        public void Teardown()
        {
            if (tornDown) return;
            tornDown = true;
            ready = false;

            for (int i = controls.Count - 1; i >= 0; i--)
            {
                try { controls[i].End(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                try { controls[i].ReleaseResources(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            controls.Clear();

            if (PrefabPool != null)
            {
                PrefabPool.Dispose();
                PrefabPool = null;
            }
            foreach (var m in materials.Values)
            {
                if (m != null) Destroy(m);
            }
            materials.Clear();
            if (RingMesh != null)
            {
                Destroy(RingMesh);
                RingMesh = null;
            }
        }

        private void OnDestroy()
        {
            Teardown();
        }

        private void BuildRingMesh()
        {
            const int segments = 40;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices[i * 2] = dir * 0.5f;
                vertices[i * 2 + 1] = dir * 0.44f;
                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = i * 2 + 1;
                triangles[t + 2] = next * 2;
                triangles[t + 3] = next * 2;
                triangles[t + 4] = i * 2 + 1;
                triangles[t + 5] = next * 2 + 1;
            }
            RingMesh = new Mesh { name = "Unit ground ring", vertices = vertices, triangles = triangles };
            RingMesh.RecalculateNormals();
            RingMesh.RecalculateBounds();
        }
    }
}
