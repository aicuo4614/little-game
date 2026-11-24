using System;
using UnityEngine;

namespace LittleGame.Client.Units.Controls
{
    /// <summary>单位模型与标准挂点控制器（参考 Demo UnitModelControl + UnitMono）。</summary>
    public sealed class UnitModelControl
    {
        public Transform Model { get; private set; }
        public Transform GroundPoint { get; private set; }
        public Transform BodyPoint { get; private set; }
        public Transform CastPoint { get; private set; }
        public Transform OverheadPoint { get; private set; }
        public float BaseModelY { get; private set; }

        public CharacterMotion Build(
            Transform root,
            int kind,
            bool isEnemy,
            bool isBoss,
            string roleKey,
            Func<string, Color, bool, Material> getMaterial,
            Func<int, Color> colorForTroop)
        {
            string fallbackName = isEnemy
                ? (kind == 2 ? "Mage" : "Barbarian")
                : kind == 0 ? "Knight" : kind == 1 ? "Ranger" : "Mage";

            var appearance = Resources.Load<UnitAppearance>("UnitAppearances/" + roleKey);
            var prefab = appearance != null ? appearance.Model : Resources.Load<GameObject>("CombatCharacters/" + fallbackName);
            float logicalHeight = appearance != null ? appearance.Height : isBoss ? 2.5f : 0.92f;

            GameObject modelObj;
            CharacterMotion motion = null;
            if (prefab != null)
            {
                modelObj = UnityEngine.Object.Instantiate(prefab, root);
                modelObj.name = appearance != null ? "Unit_" + roleKey : "KayKit_" + fallbackName;
                var renderers = modelObj.GetComponentsInChildren<Renderer>();
                if (appearance == null)
                {
                    var texture = Resources.Load<Texture2D>("CombatCharacters/" + fallbackName.ToLowerInvariant() + "_texture");
                    var material = getMaterial("KayKit_" + fallbackName, Color.white, false);
                    material.mainTexture = texture;
                    foreach (var r in renderers) r.sharedMaterial = material;
                }

                Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                float scale = logicalHeight / Mathf.Max(0.01f, bounds.size.y);
                modelObj.transform.localScale *= scale;
                modelObj.transform.localPosition = new Vector3(0f, -bounds.min.y * scale, 0f);

                motion = modelObj.AddComponent<CharacterMotion>();
                if (appearance != null) motion.Configure(appearance);
                else motion.Configure(kind);
            }
            else
            {
                modelObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                modelObj.transform.SetParent(root, false);
                modelObj.transform.localPosition = Vector3.up * 0.45f;
                modelObj.transform.localScale = new Vector3(0.48f, 0.48f, 0.48f);
                modelObj.GetComponent<Renderer>().sharedMaterial = getMaterial(
                    "Fallback_" + roleKey,
                    isEnemy ? new Color(0.65f, 0.27f, 0.23f) : colorForTroop(kind),
                    false);
                var col = modelObj.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
            }

            Model = modelObj.transform;
            BaseModelY = Model.localPosition.y;
            GroundPoint = CreateSocket(root, "Socket_Ground", new Vector3(0f, 0.04f, 0f));
            BodyPoint = CreateSocket(root, "Socket_Body", new Vector3(0f, logicalHeight * 0.58f, 0f));
            CastPoint = CreateSocket(root, "Socket_Cast", new Vector3(0f, logicalHeight * 0.65f, isEnemy ? -0.35f : 0.35f));
            OverheadPoint = CreateSocket(root, "Socket_Overhead", new Vector3(0f, logicalHeight + 0.38f, 0f));
            return motion;
        }

        public void ApplyBob(bool fightingBob, int instanceId)
        {
            if (Model == null) return;
            var p = Model.localPosition;
            p.y = BaseModelY + (fightingBob ? Mathf.Sin(Time.unscaledTime * 5f + instanceId) * 0.025f : 0f);
            Model.localPosition = p;
        }

        private static Transform CreateSocket(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }
    }
}
