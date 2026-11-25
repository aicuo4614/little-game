using System;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client.Units.Controls
{
    /// <summary>单位头顶血条、星标（1星银白、2星亮蓝、3星灿金，2/3星比1星大10%且带升星弹跳）；仅状态生效时显示临时提示圈。</summary>
    public sealed class UnitStateControl
    {
        private static readonly Color Star1WhiteColor = new Color(0.92f, 0.94f, 0.98f);
        private static readonly Color Star2BlueColor = new Color(0.32f, 0.84f, 1.0f);
        private static readonly Color Star3GoldColor = new Color(1.0f, 0.82f, 0.16f);
        private const float Star1Size = 0.035f;
        private const float StarHighSize = Star1Size * 1.10f; // 2星/3星比1星大10%

        private Camera billCam;
        private Transform healthRoot;
        private Transform healthFill;
        private TextMesh label;
        private GameObject statusRing;
        private bool isEnemyUnit;
        private int lastStar = 1;
        private float starPopTimer;

        public void Build(
            Transform root,
            int kind,
            bool isEnemy,
            bool isBoss,
            Camera camera,
            Mesh ringMesh,
            Func<string, Color, bool, Material> getMaterial,
            Func<int, Color> colorForTroop)
        {
            billCam = camera;
            isEnemyUnit = isEnemy;
            // 未选中单位不创建职业/阵营、分支或星级装饰圈；信息由模型、血条和头顶星标表达。
            statusRing = CreateGroundRing(root, "StatusRing", 0.98f, 0.98f, new Color(0.27f, 0.7f, 0.92f), ringMesh, getMaterial, 0.028f);
            statusRing.SetActive(false);

            label = CreateLabel(root, camera, isBoss ? 2.82f : 1.40f);
            healthRoot = new GameObject("Health").transform;
            healthRoot.SetParent(root, false);
            healthRoot.localPosition = new Vector3(0f, isBoss ? 2.65f : 1.22f, 0f);
            healthRoot.rotation = camera.transform.rotation;
            CreateBarCube(healthRoot, Vector3.zero, new Vector3(0.7f, 0.065f, 0.03f), new Color(0.2f, 0.24f, 0.24f), getMaterial);
            // 血条填充轴心锚定左边缘(-0.34)，缩小时严格从右向左减少，不再两头往中间缩
            healthFill = new GameObject("Fill").transform;
            healthFill.SetParent(healthRoot, false);
            healthFill.localPosition = new Vector3(-0.34f, 0f, 0f);
            CreateBarCube(healthFill, new Vector3(0.34f, 0f, -0.02f), new Vector3(0.68f, 0.05f, 0.03f),
                isEnemy ? new Color(0.85f, 0.4f, 0.32f) : new Color(0.39f, 0.8f, 0.59f), getMaterial);
        }

        public void Sync(float hpRatio, string title, bool statusActive, int star = 1, SkillBranch branch = SkillBranch.None)
        {
            if (billCam != null)
            {
                var camRot = billCam.transform.rotation;
                if (healthRoot != null) healthRoot.rotation = camRot;
                if (label != null) label.transform.rotation = camRot;
            }
            if (label != null)
            {
                label.text = title;
                if (!isEnemyUnit)
                {
                    if (star > lastStar) starPopTimer = 0.55f;
                    lastStar = star;

                    // 1星银白、2星亮蓝、3星灿金；2星与3星均比1星大10%
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
            if (healthFill != null) healthFill.localScale = new Vector3(Mathf.Clamp01(hpRatio), 1f, 1f);
            if (statusRing != null) statusRing.SetActive(statusActive);

        }

        private static GameObject CreateGroundRing(
            Transform parent,
            string ringName,
            float width,
            float depth,
            Color color,
            Mesh ringMesh,
            Func<string, Color, bool, Material> getMaterial,
            float y)
        {
            var go = new GameObject(ringName, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localScale = new Vector3(width, 1f, depth);
            go.GetComponent<MeshFilter>().sharedMesh = ringMesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = getMaterial("Ring" + color, color, true);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static TextMesh CreateLabel(Transform parent, Camera camera, float y)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.rotation = camera.transform.rotation;
            var tm = go.AddComponent<TextMesh>();
            tm.text = "";
            tm.fontSize = 36;
            tm.characterSize = Star1Size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Star1WhiteColor;
            tm.font = Resources.Load<Font>("GameFont");
            if (tm.font != null) go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
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
            r.sharedMaterial = getMaterial("Bar_" + color.ToString(), color, true);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
        }
    }
}
