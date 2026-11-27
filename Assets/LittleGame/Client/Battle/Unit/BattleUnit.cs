using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 战斗单位纯 C# 组合宿主（严格对齐 Demo BattleUnit）：
    /// 由 7 个职责单一的 BattleUnitControlBase 组合而成，完整承载 3D UnitMono + Animator + 5态有限状态机。
    /// </summary>
    public sealed class BattleUnit
    {
        private readonly List<BattleUnitControlBase> controls = new List<BattleUnitControlBase>(8);
        private bool hasPose;
        private bool ended;
        private bool released;

        public int UnitId { get; private set; }
        public int Kind { get; private set; }
        public bool IsEnemy { get; private set; }
        public bool IsBoss { get; private set; }
        public string RoleKey { get; private set; }
        public string VisualKey { get; private set; }
        public Transform Transform { get; private set; }
        public PoolGroup PrefabPool { get; private set; }
        public Camera Camera { get; private set; }
        public Mesh RingMesh { get; private set; }
        public Func<string, Color, bool, Material> GetMaterial { get; private set; }
        public Func<int, Color> ColorForTroop { get; private set; }

        public bool IsEnded => ended;
        public bool IsDead => StateControl.CurrentStateType == BattleUnitStateType.Die;
        public bool Active => Transform != null && Transform.gameObject.activeSelf;

        public float AppliedMovementSpeed { get; private set; } = -1f;
        public bool AppliedMotionPaused { get; private set; }
        public bool AppliedForceWalk { get; private set; }

        public BattleUnitModelControl ModelControl => GetControl<BattleUnitModelControl>();
        public BattleUnitAnimatorControl AnimatorControl => GetControl<BattleUnitAnimatorControl>();
        public BattleUnitStateControl StateControl => GetControl<BattleUnitStateControl>();
        public BattleUnitHeadControl HeadControl => GetControl<BattleUnitHeadControl>();
        public BattleUnitStatusControl StatusControl => GetControl<BattleUnitStatusControl>();
        public BattleUnitGraphicControl GraphicControl => GetControl<BattleUnitGraphicControl>();
        public BattleUnitTargetControl TargetControl => GetControl<BattleUnitTargetControl>();

        public Transform Model => ModelControl.Model;
        public Transform GroundPoint => ModelControl.GroundPoint;
        public Transform BodyPoint => ModelControl.BodyPoint;
        public Transform CastPoint => ModelControl.CastPoint;
        public Transform OverheadPoint => ModelControl.OverheadPoint;

        public BattleUnit(Transform root, PoolGroup prefabPool)
        {
            Transform = root;
            PrefabPool = prefabPool;
        }

        /// <summary>按依赖顺序组装全部 7 个单位控件（对齐 Demo BattleUnit.Bind）。</summary>
        public void Bind(
            int unitId,
            int kind,
            bool isEnemy,
            bool isBoss,
            string visualKey,
            string roleKey,
            Camera camera,
            Mesh ringMesh,
            Func<string, Color, bool, Material> getMaterial,
            Func<int, Color> colorForTroop)
        {
            UnitId = unitId;
            Kind = kind;
            IsEnemy = isEnemy;
            IsBoss = isBoss;
            VisualKey = visualKey;
            Camera = camera;
            RingMesh = ringMesh;
            GetMaterial = getMaterial;
            ColorForTroop = colorForTroop;
            RoleKey = roleKey;

            try
            {
                RegisterControl(new BattleUnitModelControl());
                RegisterControl(new BattleUnitAnimatorControl());
                RegisterControl(new BattleUnitStatusControl());
                RegisterControl(new BattleUnitStateControl());
                RegisterControl(new BattleUnitHeadControl());
                RegisterControl(new BattleUnitGraphicControl());
                RegisterControl(new BattleUnitTargetControl());

                for (int i = 0; i < controls.Count; i++)
                {
                    controls[i].Start();
                }
            }
            catch
            {
                End();
                ReleaseResources();
                throw;
            }
        }

        private void RegisterControl(BattleUnitControlBase control)
        {
            controls.Add(control);
            control.Init(this);
        }

        public T GetControl<T>() where T : BattleUnitControlBase
        {
            for (int i = 0; i < controls.Count; i++)
            {
                if (controls[i] is T match)
                {
                    return match;
                }
            }
            throw new InvalidOperationException("Battle unit control missing: " + typeof(T).Name);
        }

        /// <summary>同步纯规则状态到 3D 位置/朝向/状态机/血条/状态圈。</summary>
        public void SyncFromRule(
            Vector3 worldPos,
            float yawDegrees,
            float hpRatio,
            string title,
            bool statusActive,
            float rootScale,
            bool alive,
            bool visibleWhenDead,
            bool fightingBob,
            bool motionPaused,
            bool controlled,
            int star = 1,
            bool forceWalk = false,
            float movementSpeed = -1f,
            float battleSpeed = 1f,
            float manaRatio = 0f,
            bool showMana = false)
        {
            if (ended || Transform == null)
            {
                return;
            }

            bool show = alive || visibleWhenDead || !StateControl.DeathCompleted;
            if (Transform.gameObject.activeSelf != show)
            {
                Transform.gameObject.SetActive(show);
            }

            var rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            if (!hasPose || !fightingBob || (Transform.position - worldPos).sqrMagnitude > 9f)
            {
                Transform.SetPositionAndRotation(worldPos, rotation);
                hasPose = true;
            }
            else if (!motionPaused)
            {
                float posBlend = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
                float rotBlend = 1f - Mathf.Exp(-38f * Time.unscaledDeltaTime);
                Transform.position = Vector3.Lerp(Transform.position, worldPos, posBlend);
                Transform.rotation = Quaternion.Slerp(Transform.rotation, rotation, rotBlend);
            }

            Transform.localScale = Vector3.one * rootScale;

            AppliedMovementSpeed = movementSpeed;
            AppliedMotionPaused = motionPaused;
            AppliedForceWalk = forceWalk;

            AnimatorControl.SetBattleSpeed(battleSpeed);
            AnimatorControl.SetPaused(motionPaused);

            bool moving = forceWalk || (movementSpeed > 0.12f);
            StateControl.SyncLocomotion(alive, controlled, moving);
            HeadControl.Sync(hpRatio, manaRatio, showMana, title, star);
            StatusControl.SetStatusActive(statusActive && alive);
            ModelControl.ApplyWalkBob((fightingBob || forceWalk) && alive && !motionPaused && !controlled, Transform.GetInstanceID());
        }

        /// <summary>将本单位适配为 HotUpdate_UnitCore 共享 TimelinePlayback 的角色对象。</summary>
        public BattleUnitTimelineActor ToTimelineActor()
        {
            var actor = new BattleUnitTimelineActor(
                Transform,
                Model != null ? Model : Transform,
                AnimatorControl.Animator,
                () => Camera != null ? Camera.transform.right : Vector3.right,
                ModelControl.FindTimelineNode,
                Transform.position);
            actor.Team = IsEnemy ? UnitTeamType.TeamB : UnitTeamType.TeamA;
            return actor;
        }

        public void Tick(float deltaTime)
        {
            if (ended) return;
            for (int i = 0; i < controls.Count; i++)
            {
                controls[i].Tick(deltaTime);
            }
        }

        public void End()
        {
            if (ended) return;
            ended = true;
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                controls[i].End();
            }
        }

        public void ReleaseResources()
        {
            if (released) return;
            released = true;
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                controls[i].ReleaseResources();
            }
            controls.Clear();
            if (Transform != null)
            {
                UnityEngine.Object.Destroy(Transform.gameObject);
                Transform = null;
            }
        }

        public object Snapshot()
        {
            var tr = Transform;
            var model = Model;
            return new
            {
                id = UnitId,
                role = RoleKey,
                model = model != null ? model.name : "",
                active = Active,
                x = tr != null ? tr.position.x : 0f,
                z = tr != null ? tr.position.z : 0f,
                facingYaw = tr != null ? tr.eulerAngles.y : 0f,
                movementSpeed = AppliedMovementSpeed,
                motionPaused = AppliedMotionPaused,
                forceWalk = AppliedForceWalk,
                fsmState = StateControl.CurrentStateType.ToString(),
                hasAnimator = AnimatorControl.HasValidController,
                hasUnitMono = ModelControl.UnitMono != null,
                hitStopped = AnimatorControl.IsHitStopped,
                groundMarkers = tr != null
                    ? tr.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(r => r.name == "FactionRing" || r.name == "BranchRing" || r.name == "Star3CrownRing" || r.name == "StatusRing")
                        .Select(r => new { name = r.name, active = r.gameObject.activeInHierarchy && r.enabled })
                    : Enumerable.Empty<object>(),
                action = AnimatorControl.CurrentAction,
                actionTime = AnimatorControl.ActionTime,
                animationPlaying = AnimatorControl.IsPlaying,
                renderers = model != null
                    ? model.GetComponentsInChildren<Renderer>().Select(r => new
                    {
                        type = r.GetType().Name,
                        visible = r.isVisible,
                        shaders = r.sharedMaterials.Select(m => m != null && m.shader != null ? m.shader.name : "missing"),
                        supported = r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.isSupported)
                    })
                    : Enumerable.Empty<object>(),
                bonePose = model != null
                    ? model.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r => r.bones).Where(b => b != null).Distinct().Take(24)
                        .Select(b => new { name = b.name, x = b.localRotation.x, y = b.localRotation.y, z = b.localRotation.z, w = b.localRotation.w }).ToArray()
                    : Array.Empty<object>(),
                upperArm = model != null
                    ? model.GetComponentsInChildren<Transform>()
                        .Where(t => t.name == "upperarm.l" || t.name == "upperarm_l" || t.name == "Body")
                        .Select(t => new { x = t.localEulerAngles.x, y = t.localEulerAngles.y, z = t.localEulerAngles.z })
                    : Enumerable.Empty<object>()
            };
        }
    }
}
