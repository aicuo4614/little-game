using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client.Battle
{
    /// <summary>单位头顶血条与星标控件（对齐 Demo BattleUnitHeadControl，与行为状态机完全解耦）。</summary>
    public sealed class BattleUnitHeadControl : BattleUnitControlBase
    {
        private static readonly Color Star1WhiteColor = new Color(0.92f, 0.94f, 0.98f);
        private static readonly Color Star2BlueColor = new Color(0.32f, 0.84f, 1.0f);
        private static readonly Color Star3GoldColor = new Color(1.0f, 0.82f, 0.16f);
        private const float Star1Size = 0.035f;
        private const float StarHighSize = Star1Size * 1.10f;

        private Transform healthRoot;
        private Transform healthFill;
        private TextMesh label;
        private int lastStar = 1;
        private float starPopTimer;

        public override void Init(BattleUnit owner)
        {
            base.Init(owner);

            var camRot = owner.Camera != null ? owner.Camera.transform.rotation : Quaternion.identity;
            label = CreateLabel(owner.Transform, camRot, owner.IsBoss ? 2.82f : 1.40f);

            healthRoot = new GameObject("Health").transform;
            healthRoot.SetParent(owner.Transform, false);
            healthRoot.localPosition = new Vector3(0f, owner.IsBoss ? 2.65f : 1.22f, 0f);
            healthRoot.rotation = camRot;
            CreateBarCube(healthRoot, Vector3.zero, new Vector3(0.7f, 0.065f, 0.03f), new Color(0.2f, 0.24f, 0.24f), owner.GetMaterial);

            healthFill = new GameObject("Fill").transform;
            healthFill.SetParent(healthRoot, false);
            healthFill.localPosition = new Vector3(-0.34f, 0f, 0f);
            CreateBarCube(
                healthFill,
                new Vector3(0.34f, 0f, -0.02f),
                new Vector3(0.68f, 0.05f, 0.03f),
                owner.IsEnemy ? new Color(0.85f, 0.4f, 0.32f) : new Color(0.39f, 0.8f, 0.59f),
                owner.GetMaterial);
        }

        public void Sync(float hpRatio, string title, int star)
        {
            bool visible = !Owner.IsDead;

            if (healthRoot != null) healthRoot.gameObject.SetActive(visible);
            if (label != null) label.gameObject.SetActive(visible);

            if (Owner.Camera != null)
            {
                var camRot = Owner.Camera.transform.rotation;
                if (healthRoot != null) healthRoot.rotation = camRot;
                if (label != null) label.transform.rotation = camRot;
            }

            if (label != null)
            {
                label.text = title ?? string.Empty;
                if (!Owner.IsEnemy)
                {
                    if (star > lastStar) starPopTimer = 0.55f;
                    lastStar = star;

                    label.color = star >= 3 ? Star3GoldColor : star == 2 ? Star2BlueColor : Star1WhiteColor;
                    label.characterSize = star >= 2 ? StarHighSize : Star1Size;
                    label.fontStyle = star >= 2 ? FontStyle.Bold : FontStyle.Normal;

                    if (starPopTimer > 0f)
                    {
                        starPopTimer = Mathf.Max(0f, starPopTimer - Time.unscaledDeltaTime);
                        float progress = 1f - starPopTimer / 0.55f;
                        float pop = 1f + Mathf.Sin(progress * Mathf.PI) * 0.52f * (1f - progress * 0.4f);
                        label.transform.localScale = Vector3.one * pop;
                    }
                    else
                    {
                        label.transform.localScale = Vector3.one;
                    }
                }
            }

            if (healthFill != null)
            {
                healthFill.localScale = new Vector3(Mathf.Clamp01(hpRatio), 1f, 1f);
            }
        }

        private static TextMesh CreateLabel(Transform parent, Quaternion camRot, float y)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.rotation = camRot;
            var tm = go.AddComponent<TextMesh>();
            tm.text = "";
            tm.fontSize = 36;
            tm.characterSize = Star1Size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Star1WhiteColor;
            tm.font = Resources.Load<Font>("GameFont");
            if (tm.font != null)
            {
                go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            }
            return tm;
        }

        private static void CreateBarCube(
            Transform parent,
            Vector3 localPos,
            Vector3 scale,
            Color color,
            Func<string, Color, bool, Material> getMaterial)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (getMaterial != null)
            {
                r.sharedMaterial = getMaterial("Bar_" + color, color, true);
            }
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
        }
    }
}
