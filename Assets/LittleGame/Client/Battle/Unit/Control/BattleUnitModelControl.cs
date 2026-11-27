using System;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 战斗单位 3D 模型与标准挂点控件（对齐 Demo BattleUnitModelControl + UnitMono）：
    /// 优先通过 UnitAppearance / Package_Unit 加载带 UnitMono + Animator 的 3D 预制体并接入对象池。
    /// </summary>
    public sealed class BattleUnitModelControl : BattleUnitControlBase
    {
        private GameObject modelInstance;
        private GameObject modelPrefab;
        private bool pooled;
        private bool appearanceLoaded;
        private Renderer[] renderers = Array.Empty<Renderer>();

        public Transform Model { get; private set; }
        public UnitMono UnitMono { get; private set; }
        public Animator Animator { get; private set; }
        public float LogicalHeight { get; private set; }
        public float BaseModelY { get; private set; }

        public Transform GroundPoint { get; private set; }
        public Transform BodyPoint { get; private set; }
        public Transform CastPoint { get; private set; }
        public Transform OverheadPoint { get; private set; }
        public Renderer[] Renderers => renderers;

        public override void Init(BattleUnit owner)
        {
            base.Init(owner);

            var appearance = UnitAppearance.Load(owner.RoleKey);
            appearanceLoaded = appearance != null;
            modelPrefab = appearance != null && appearance.Model != null
                ? appearance.Model
                : null;
            LogicalHeight = appearance != null ? appearance.Height : (owner.IsBoss ? 2.5f : 1.05f);

            if (modelPrefab != null)
            {
                if (owner.PrefabPool != null)
                {
                    modelInstance = owner.PrefabPool.Spawn("Unit/Low/" + owner.RoleKey + "/" + owner.RoleKey, owner.Transform, ResourcePackageName.UnitPackage);
                    pooled = true;
                }
                else
                {
                    modelInstance = UnityEngine.Object.Instantiate(modelPrefab, owner.Transform);
                    pooled = false;
                }

                modelInstance.name = "Unit_" + owner.RoleKey;
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = modelPrefab.transform.localScale;
                modelInstance.SetActive(true);

                // 确保具备 UnitMono 挂点与真实 3D Animator
                UnitMono = UnitMonoBinder.EnsureBound(modelInstance);
                UnitMono.InitTeamType(owner.IsEnemy ? UnitTeamType.TeamB : UnitTeamType.TeamA);

                Animator = UnitMono.Animator != null
                    ? UnitMono.Animator
                    : modelInstance.GetComponentInChildren<Animator>(true);
                if (Animator == null)
                {
                    Animator = modelInstance.AddComponent<Animator>();
                }
                if (Animator.runtimeAnimatorController == null && appearance != null && appearance.AnimatorController != null)
                {
                    Animator.runtimeAnimatorController = appearance.AnimatorController;
                }
                Animator.applyRootMotion = false;
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Animator.enabled = true;
                Animator.speed = 1f;
                Animator.Rebind();
                Animator.Update(0f);

                renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                    float h = Mathf.Max(0.05f, bounds.size.y);
                    float scale = LogicalHeight / h;
                    // 仅在模型原始高度与逻辑高度差异超过 8% 时归一化缩放（已标准化的 UnitModels 预制体保持原比例）
                    if (Mathf.Abs(scale - 1f) > 0.08f)
                    {
                        modelInstance.transform.localScale *= scale;
                        modelInstance.transform.localPosition = new Vector3(0f, -bounds.min.y * scale, 0f);
                    }
                }
            }
            else
            {
                modelInstance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                modelInstance.name = "Fallback_" + owner.RoleKey;
                modelInstance.transform.SetParent(owner.Transform, false);
                modelInstance.transform.localPosition = Vector3.up * 0.45f;
                modelInstance.transform.localScale = new Vector3(0.48f, 0.48f, 0.48f);
                var col = modelInstance.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
                var r = modelInstance.GetComponent<Renderer>();
                if (r != null && owner.GetMaterial != null)
                {
                    r.sharedMaterial = owner.GetMaterial(
                        "Fallback_" + owner.RoleKey,
                        owner.IsEnemy ? new Color(0.65f, 0.27f, 0.23f) : owner.ColorForTroop(owner.Kind),
                        false);
                }
                renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                UnitMono = UnitMonoBinder.EnsureBound(modelInstance);
            }

            Model = modelInstance.transform;
            BaseModelY = Model.localPosition.y;

            GroundPoint = ResolveOrCreateSocket(UnitNodeType.Root, "Socket_Ground", new Vector3(0f, 0.04f, 0f));
            BodyPoint = ResolveOrCreateSocket(UnitNodeType.BodyCenter, "Socket_Body", new Vector3(0f, LogicalHeight * 0.56f, 0f));
            CastPoint = ResolveOrCreateSocket(UnitNodeType.Custom1, "Socket_Cast", new Vector3(0f, LogicalHeight * 0.65f, 0.35f));
            OverheadPoint = ResolveOrCreateSocket(UnitNodeType.Head, "Socket_Overhead", new Vector3(0f, LogicalHeight + 0.35f, 0f));
        }

        public Transform FindTimelineNode(UnitNodeType nodeType)
        {
            if (UnitMono != null)
            {
                var found = UnitMono.FindNodeTransform(nodeType);
                if (found != null)
                {
                    return found;
                }
            }

            return nodeType switch
            {
                UnitNodeType.BodyCenter or UnitNodeType.Spine => BodyPoint ?? Owner.Transform,
                UnitNodeType.Head => OverheadPoint ?? Owner.Transform,
                UnitNodeType.Custom1 or UnitNodeType.RightHand or UnitNodeType.LeftHand => CastPoint ?? BodyPoint ?? Owner.Transform,
                _ => GroundPoint ?? Owner.Transform,
            };
        }

        public void SetVisible(bool visible)
        {
            if (modelInstance != null && modelInstance.activeSelf != visible)
            {
                modelInstance.SetActive(visible);
            }
        }

        public void ApplyWalkBob(bool active, int seed)
        {
            if (Model == null) return;
            var p = Model.localPosition;
            p.y = BaseModelY + (active ? Mathf.Sin(Time.unscaledTime * 5f + seed) * 0.02f : 0f);
            Model.localPosition = p;
        }

        public override void ReleaseResources()
        {
            if (modelInstance != null)
            {
                if (Animator != null)
                {
                    Animator.enabled = true;
                    Animator.speed = 1f;
                }

                if (pooled && Owner.PrefabPool != null && modelPrefab != null)
                {
                    modelInstance.SetActive(false);
                    Owner.PrefabPool.Despawn(modelInstance);
                }
                else
                {
                    UnityEngine.Object.Destroy(modelInstance);
                }

                modelInstance = null;
            }

            Model = null;
            if (appearanceLoaded)
            {
                UnitAppearance.Release(Owner.RoleKey);
                appearanceLoaded = false;
            }
            UnitMono = null;
            Animator = null;
            renderers = Array.Empty<Renderer>();
        }

        private Transform ResolveOrCreateSocket(UnitNodeType nodeType, string fallbackName, Vector3 localPos)
        {
            if (UnitMono != null)
            {
                var node = UnitMono.FindNodeTransform(nodeType);
                if (node != null && node != Owner.Transform && node != Model)
                {
                    return node;
                }
            }

            var t = new GameObject(fallbackName).transform;
            t.SetParent(Owner.Transform, false);
            t.localPosition = localPos;
            return t;
        }
    }
}
