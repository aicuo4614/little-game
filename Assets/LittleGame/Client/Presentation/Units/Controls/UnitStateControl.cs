using System;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client.Units.Controls
{
    /// <summary>单位头顶血条、星标、阵营环、二星分支符文环与三星金环控制器（参考 Demo UnitStateControl）。</summary>
    public sealed class UnitStateControl
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Camera billCam;
        private Transform healthRoot;
        private Transform healthFill;
        private TextMesh label;
        private GameObject statusRing;
        private MeshRenderer branchRingRenderer;
        private GameObject branchRingObj;
        private GameObject star3CrownRing;
        private MaterialPropertyBlock branchBlock;
        private SkillBranch lastBranch = SkillBranch.None;

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
            CreateGroundRing(root, "FactionRing", isBoss ? 2.6f : 0.75f, isBoss ? 1.6f : 0.75f,
                isEnemy ? new Color(0.65f, 0.24f, 0.19f) : colorForTroop(kind), ringMesh, getMaterial, 0.025f);
            statusRing = CreateGroundRing(root, "StatusRing", 0.98f, 0.98f, new Color(0.27f, 0.7f, 0.92f), ringMesh, getMaterial, 0.028f);
            statusRing.SetActive(false);

            if (!isEnemy)
            {
                branchRingObj = CreateGroundRing(root, "BranchRing", 0.88f, 0.88f, Color.white, ringMesh, getMaterial, 0.031f);
                branchRingRenderer = branchRingObj.GetComponent<MeshRenderer>();
                branchBlock = new MaterialPropertyBlock();
                branchRingObj.SetActive(false);

                star3CrownRing = CreateGroundRing(root, "Star3GoldRing", 1.06f, 1.06f, new Color(1.0f, 0.84f, 0.28f), ringMesh, getMaterial, 0.033f);
                star3CrownRing.SetActive(false);
            }

            label = CreateLabel(root, camera, isBoss ? 2.82f : 1.38f);
            healthRoot = new GameObject("Health").transform;
            healthRoot.SetParent(root, false);
            healthRoot.localPosition = new Vector3(0f, isBoss ? 2.65f : 1.22f, 0f);
            healthRoot.rotation = camera.transform.rotation;
            CreateBarCube(healthRoot, Vector3.zero, new Vector3(0.7f, 0.065f, 0.03f), new Color(0.2f, 0.24f, 0.24f), getMaterial);
            healthFill = new GameObject("Fill").transform;
            healthFill.SetParent(healthRoot, false);
            CreateBarCube(healthFill, new Vector3(0f, 0f, -0.02f), new Vector3(0.68f, 0.05f, 0.03f),
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
            if (label != null) label.text = title;
            if (healthFill != null) healthFill.localScale = new Vector3(Mathf.Clamp01(hpRatio), 1f, 1f);
            if (statusRing != null) statusRing.SetActive(statusActive);

            if (branchRingObj != null)
            {
                bool hasBranch = branch != SkillBranch.None;
                branchRingObj.SetActive(hasBranch);
                if (hasBranch && branch != lastBranch && branchRingRenderer != null)
                {
                    lastBranch = branch;
                    Color bc = ColorForBranch(branch);
                    branchBlock.SetColor(BaseColorId, bc);
                    branchRingRenderer.SetPropertyBlock(branchBlock);
                }
            }
            if (star3CrownRing != null)
                star3CrownRing.SetActive(star >= 3);
        }

        private static Color ColorForBranch(SkillBranch branch)
        {
            switch (branch)
            {
                case SkillBranch.Bastion: return new Color(0.30f, 0.76f, 1.0f);
                case SkillBranch.WarCry: return new Color(1.0f, 0.68f, 0.24f);
                case SkillBranch.Pierce: return new Color(1.0f, 0.88f, 0.32f);
                case SkillBranch.Gale: return new Color(0.32f, 1.0f, 0.72f);
                case SkillBranch.StormPool: return new Color(0.64f, 0.42f, 1.0f);
                case SkillBranch.ChainLightning: return new Color(0.45f, 0.92f, 1.0f);
                case SkillBranch.DeepFreeze: return new Color(0.32f, 0.72f, 1.0f);
                case SkillBranch.IceArmor: return new Color(0.72f, 0.96f, 1.0f);
                default: return Color.white;
            }
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
            tm.fontSize = 32;
            tm.characterSize = 0.035f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.96f, 0.97f, 0.9f);
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
            go.GetComponent<Renderer>().sharedMaterial = getMaterial(color.ToString(), color, false);
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
        }
    }
}
