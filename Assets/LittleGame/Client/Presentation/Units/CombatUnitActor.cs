using System;
using System.Linq;
using LittleGame.Client.Units.Controls;
using UnityEngine;

namespace LittleGame.Client.Units
{
    /// <summary>
    /// 单个战场单位的表现宿主（对齐 Demo Unit 组合分层）：
    /// 由 UnitModelControl、UnitAnimatorControl、UnitStateControl 三个职责单一的控制器组合而成。
    /// </summary>
    public sealed class CombatUnitActor : MonoBehaviour
    {
        public int UnitId { get; private set; }
        public bool IsEnemy { get; private set; }
        public bool IsBoss { get; private set; }
        public string RoleKey { get; private set; }
        public string VisualKey { get; private set; }

        public UnitModelControl ModelControl { get; private set; }
        public UnitAnimatorControl AnimatorControl { get; private set; }
        public UnitStateControl StateControl { get; private set; }

        public Transform Model => ModelControl?.Model;
        public CharacterMotion Motion => AnimatorControl?.Motion;
        public Transform GroundPoint => ModelControl?.GroundPoint;
        public Transform BodyPoint => ModelControl?.BodyPoint;
        public Transform CastPoint => ModelControl?.CastPoint;
        public Transform OverheadPoint => ModelControl?.OverheadPoint;

        public void Initialize(
            int unitId,
            int kind,
            bool isEnemy,
            bool isBoss,
            string visualKey,
            Camera camera,
            Mesh ringMesh,
            Func<string, Color, bool, Material> getMaterial,
            Func<int, Color> colorForTroop)
        {
            UnitId = unitId;
            IsEnemy = isEnemy;
            IsBoss = isBoss;
            VisualKey = visualKey;
            RoleKey = isEnemy
                ? new[] { "Grunt", "Runner", "Armored", "Healer", "Giant" }[Mathf.Clamp(kind, 0, 4)]
                : new[] { "Shield", "Archer", "Storm", "Frost" }[Mathf.Clamp(kind, 0, 3)];

            ModelControl = new UnitModelControl();
            var motion = ModelControl.Build(transform, kind, isEnemy, isBoss, RoleKey, getMaterial, colorForTroop);
            AnimatorControl = new UnitAnimatorControl(motion);
            StateControl = new UnitStateControl();
            StateControl.Build(transform, kind, isEnemy, isBoss, camera, ringMesh, getMaterial, colorForTroop);
        }

        public void SyncState(
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
            int star = 1,
            LittleGame.Combat.SkillBranch branch = LittleGame.Combat.SkillBranch.None,
            bool forceWalk = false)
        {
            bool show = alive || visibleWhenDead || (AnimatorControl != null && !AnimatorControl.DeathFinished);
            gameObject.SetActive(show);
            transform.position = worldPos;
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            transform.localScale = Vector3.one * rootScale;

            StateControl?.Sync(hpRatio, title, statusActive, star, branch);
            ModelControl?.ApplyBob(fightingBob || forceWalk, transform.GetInstanceID());
            AnimatorControl?.Sync(alive, motionPaused, forceWalk);
        }

        public object Snapshot()
        {
            return new
            {
                id = UnitId,
                role = RoleKey,
                model = Model != null ? Model.name : "",
                active = gameObject.activeSelf,
                action = AnimatorControl != null ? AnimatorControl.CurrentAction : null,
                actionTime = AnimatorControl != null ? AnimatorControl.ActionTime : 0f,
                animationPlaying = Model != null && Model.GetComponent<Animation>() != null && Model.GetComponent<Animation>().isPlaying,
                renderers = Model != null
                    ? Model.GetComponentsInChildren<Renderer>().Select(r => new
                    {
                        type = r.GetType().Name,
                        visible = r.isVisible,
                        shaders = r.sharedMaterials.Select(m => m != null && m.shader != null ? m.shader.name : "missing"),
                        supported = r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.isSupported)
                    })
                    : Enumerable.Empty<object>(),
                upperArm = Model != null
                    ? Model.GetComponentsInChildren<Transform>()
                        .Where(t => t.name == "upperarm.l" || t.name == "upperarm_l" || t.name == "Body")
                        .Select(t => new { x = t.localEulerAngles.x, y = t.localEulerAngles.y, z = t.localEulerAngles.z })
                    : Enumerable.Empty<object>()
            };
        }
    }
}
