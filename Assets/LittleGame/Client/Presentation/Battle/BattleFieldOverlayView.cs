using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 横向战场持续态可视化与场景氛围控制器（只读规则状态，无碰撞体）：
    /// 1. 整体白色布阵框（平时隐藏，拖拽场上或候补单位时显现，拖出范围边界变红提示越界）；
    /// 2. 持续雷池法阵、集火准星、首领横向蓄力预警带；
    /// 3. 选中单位脚底光标；
    /// 4. 技能大招场景压暗（SceneDarken）。
    /// </summary>
    public sealed class BattleFieldOverlayView : MonoBehaviour
    {
        private static readonly Color BaseSky = new Color(0.65f, 0.71f, 0.82f);
        private static readonly Color BaseEquator = new Color(0.57f, 0.59f, 0.62f);
        private static readonly Color BaseGround = new Color(0.32f, 0.35f, 0.40f);
        private static readonly Color BaseBg = BattleView.BattleBackgroundColor;
        private const float BaseSunIntensity = 1.18f;

        private Light mainSun;
        private Camera mainCamera;
        private Mesh ringMesh;
        private readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        // 拖拽期整体白色布阵框 + 落点指示环
        private Transform deployBoxRoot;
        private readonly List<Material> deployBorderMats = new List<Material>();
        private Material deployFillMat;
        private Transform dragSpotRing;
        private Material dragSpotMat;

        internal const float BossTelegraphCenterX = .4f;
        internal const float BossTelegraphLength = 11.6f;

        // 首领横向预警带
        private Transform telegraphRoot;
        private Transform telegraphFill;
        private Material telegraphBaseMat;
        private Material telegraphFillMat;

        // 持续集火准星
        private Transform focusMarkerRoot;
        private Transform focusInnerRing;

        // 选中单位脚底高亮光标双环
        private Transform selectionCursorRoot;
        private Transform selectionInnerRing;

        // 持续雷池法阵池
        private readonly List<Transform> zoneRings = new List<Transform>();

        private float currentDarken;
        private float targetDarken;

        public void Initialize(Light sun, Camera cam, Mesh sharedRingMesh)
        {
            mainSun = sun;
            mainCamera = cam;
            ringMesh = sharedRingMesh;

            // 1. 整体白色布阵范围框（平时隐藏，拖拽任意单位时显示）
            BuildWhiteDeployBox();

            // 2. 首领横向预警带
            telegraphRoot = new GameObject("BossTelegraphDynamic").transform;
            telegraphRoot.SetParent(transform, false);
            var baseQuad = CreateFlatQuad(telegraphRoot, "TelegraphBase", Vector3.zero, new Vector3(BossTelegraphLength, 0.02f, 2.25f), new Color(0.85f, 0.18f, 0.14f, 0.40f));
            telegraphBaseMat = baseQuad.GetComponent<Renderer>().sharedMaterial;
            telegraphFill = CreateFlatQuad(telegraphRoot, "TelegraphProgress", new Vector3(0f, 0.008f, 0f), new Vector3(11.4f, 0.02f, 2.05f), new Color(1f, 0.34f, 0.18f, 0.78f)).transform;
            telegraphFillMat = telegraphFill.GetComponent<Renderer>().sharedMaterial;
            telegraphRoot.gameObject.SetActive(false);

            // 3. 持续集火旋转双环准星
            focusMarkerRoot = new GameObject("FocusReticle").transform;
            focusMarkerRoot.SetParent(transform, false);
            CreateRingObject(focusMarkerRoot, "FocusOuter", 1.28f, new Color(1f, 0.25f, 0.22f, 0.92f));
            focusInnerRing = CreateRingObject(focusMarkerRoot, "FocusInner", 0.86f, new Color(1f, 0.72f, 0.25f, 0.95f)).transform;
            focusMarkerRoot.gameObject.SetActive(false);

            // 4. 选中单位脚底高亮光标双环
            selectionCursorRoot = new GameObject("SelectionFootCursor").transform;
            selectionCursorRoot.SetParent(transform, false);
            CreateRingObject(selectionCursorRoot, "SelectOuter", 1.40f, new Color(1f, 0.88f, 0.28f, 0.96f));
            selectionInnerRing = CreateRingObject(selectionCursorRoot, "SelectInner", 1.06f, new Color(0.55f, 0.95f, 1f, 0.90f)).transform;
            selectionCursorRoot.gameObject.SetActive(false);
        }

        private void BuildWhiteDeployBox()
        {
            float cx = (BattleDefinitions.DeployMinX + BattleDefinitions.DeployMaxX) * 0.5f;
            float cz = (BattleDefinitions.DeployMinZ + BattleDefinitions.DeployMaxZ) * 0.5f;
            float w = BattleDefinitions.DeployMaxX - BattleDefinitions.DeployMinX;
            float d = BattleDefinitions.DeployMaxZ - BattleDefinitions.DeployMinZ;
            const float border = 0.095f;

            deployBoxRoot = new GameObject("WhiteDeployBox").transform;
            deployBoxRoot.SetParent(transform, false);
            deployBoxRoot.position = new Vector3(cx, 0.142f, cz);

            var fill = CreateFlatQuad(deployBoxRoot, "DeployBoxFill", Vector3.zero, new Vector3(w, 0.012f, d), new Color(1f, 1f, 1f, 0.11f));
            deployFillMat = fill.GetComponent<Renderer>().sharedMaterial;

            var top = CreateFlatQuad(deployBoxRoot, "BorderTop", new Vector3(0f, 0.01f, d * 0.5f), new Vector3(w + border, 0.018f, border), new Color(1f, 1f, 1f, 0.94f));
            var bot = CreateFlatQuad(deployBoxRoot, "BorderBot", new Vector3(0f, 0.01f, -d * 0.5f), new Vector3(w + border, 0.018f, border), new Color(1f, 1f, 1f, 0.94f));
            var left = CreateFlatQuad(deployBoxRoot, "BorderLeft", new Vector3(-w * 0.5f, 0.01f, 0f), new Vector3(border, 0.018f, d + border), new Color(1f, 1f, 1f, 0.94f));
            var right = CreateFlatQuad(deployBoxRoot, "BorderRight", new Vector3(w * 0.5f, 0.01f, 0f), new Vector3(border, 0.018f, d + border), new Color(1f, 1f, 1f, 0.94f));
            deployBorderMats.Add(top.GetComponent<Renderer>().sharedMaterial);
            deployBorderMats.Add(bot.GetComponent<Renderer>().sharedMaterial);
            deployBorderMats.Add(left.GetComponent<Renderer>().sharedMaterial);
            deployBorderMats.Add(right.GetComponent<Renderer>().sharedMaterial);
            deployBoxRoot.gameObject.SetActive(false);

            var spot = CreateRingObject(transform, "DragSpotRing", 1.15f, new Color(1f, 1f, 1f, 0.92f));
            dragSpotRing = spot.transform;
            dragSpotMat = spot.GetComponent<MeshRenderer>().sharedMaterial;
            dragSpotRing.gameObject.SetActive(false);
        }

        /// <summary>拖拽单位时显示白色布阵范围框与夹紧最近合法点 A 的落点环（拖出框外不再变红，而是平稳吸附至最近合法点 A）。</summary>
        public void SetDragZoneVisual(bool dragging, Vector3 clampedPos, bool showSpot, bool isReplace)
        {
            if (deployBoxRoot != null) deployBoxRoot.gameObject.SetActive(dragging);
            if (dragSpotRing != null) dragSpotRing.gameObject.SetActive(dragging && showSpot);
            if (!dragging) return;

            Color borderCol = new Color(1f, 1f, 1f, 0.95f);
            Color fillCol = new Color(1f, 1f, 1f, 0.11f);

            for (int i = 0; i < deployBorderMats.Count; i++) ApplyMatColor(deployBorderMats[i], borderCol);
            ApplyMatColor(deployFillMat, fillCol);

            if (dragSpotRing != null && showSpot)
            {
                dragSpotRing.position = new Vector3(clampedPos.x, 0.155f, clampedPos.z);
                ApplyMatColor(dragSpotMat, isReplace ? new Color(1f, 0.86f, 0.34f, 0.94f) : new Color(0.55f, 1f, 0.78f, 0.92f));
            }
        }

        public void SyncSelectionCursor(int selectedUnitId, bool visible, BattleUnitsControl registry)
        {
            if (selectionCursorRoot == null) return;
            bool show = visible && selectedUnitId > 0 && registry != null
                        && registry.TryGet(selectedUnitId, out var actor) && actor != null && actor.Active;
            selectionCursorRoot.gameObject.SetActive(show);
            if (!show) return;
            registry.TryGet(selectedUnitId, out var a);
            float t = Time.unscaledTime;
            float pulse = 0.94f + 0.10f * Mathf.Sin(t * 7.5f);
            selectionCursorRoot.position = new Vector3(a.Transform.position.x, 0.152f, a.Transform.position.z);
            selectionCursorRoot.localScale = Vector3.one * pulse;
            selectionCursorRoot.rotation = Quaternion.Euler(0f, t * 55f, 0f);
            if (selectionInnerRing != null) selectionInnerRing.localRotation = Quaternion.Euler(0f, -t * 110f, 0f);
        }

        public void SetTargetSceneDarken(float strength) => targetDarken = Mathf.Clamp01(strength);

        public void Sync(BattleSession session, BattleUnitsControl registry, float unscaledDt)
        {
            if (session == null) return;
            bool fighting = session.Phase == RunPhase.Fighting;
            float t = Time.unscaledTime;

            // 1. 首领横向预警带
            bool showBossWarn = fighting && session.SlamLane >= 0;
            telegraphRoot.gameObject.SetActive(showBossWarn);
            if (showBossWarn)
            {
                float laneZ = BattleDefinitions.CellZ(session.SlamLane);
                telegraphRoot.position = new Vector3(BossTelegraphCenterX, 0.135f, laneZ);
                float remain = Mathf.Max(0f, session.SlamAt - session.BattleTime);
                float progress = Mathf.Clamp01(1f - remain / 2f);
                float pulse = 0.55f + 0.45f * Mathf.Sin(t * 14f);
                ApplyMatColor(telegraphBaseMat, new Color(0.88f, 0.18f, 0.14f, 0.28f + 0.25f * pulse));
                if (telegraphFill != null)
                {
                    float len = Mathf.Max(0.25f, 11.4f * progress);
                    telegraphFill.localScale = new Vector3(len, 0.02f, 2.05f);
                    telegraphFill.localPosition = new Vector3((11.4f - len) * 0.5f, 0.008f, 0f);
                }
                ApplyMatColor(telegraphFillMat, Color.Lerp(new Color(1f, 0.42f, 0.18f, 0.65f), new Color(1f, 0.15f, 0.12f, 0.92f), progress));
            }

            // 2. 持续集火准星
            bool showFocus = fighting && session.FocusTarget > 0 && session.FocusUntil > session.BattleTime
                             && registry != null && registry.TryGet(session.FocusTarget, out var focusActor)
                             && focusActor != null && focusActor.Active;
            focusMarkerRoot.gameObject.SetActive(showFocus);
            if (showFocus && registry.TryGet(session.FocusTarget, out var fa))
            {
                focusMarkerRoot.position = new Vector3(fa.Transform.position.x, 0.155f, fa.Transform.position.z);
                float scale = 0.95f + 0.10f * Mathf.Sin(t * 10f);
                focusMarkerRoot.localScale = Vector3.one * (fa.IsBoss ? 1.95f * scale : scale);
                focusMarkerRoot.rotation = Quaternion.Euler(0f, t * 90f, 0f);
                if (focusInnerRing != null) focusInnerRing.localRotation = Quaternion.Euler(0f, -t * 180f, 0f);
            }

            // 3. 持续雷池法阵
            var zones = session.Zones;
            int activeZones = fighting && zones != null ? zones.Count : 0;
            bool wideStorm = session.Rewards != null && session.Rewards.Contains(RewardKind.WideStorm);
            float radius = wideStorm ? 1.85f : 1.35f;

            while (zoneRings.Count < activeZones)
            {
                var zr = new GameObject("StormZoneOverlay_" + zoneRings.Count).transform;
                zr.SetParent(transform, false);
                CreateRingObject(zr, "OuterArc", 2.0f, new Color(0.56f, 0.40f, 1f, 0.78f));
                CreateRingObject(zr, "InnerArc", 1.35f, new Color(0.45f, 0.85f, 1f, 0.72f));
                zoneRings.Add(zr);
            }

            for (int i = 0; i < zoneRings.Count; i++)
            {
                bool active = i < activeZones;
                zoneRings[i].gameObject.SetActive(active);
                if (!active) continue;
                var z = zones[i];
                zoneRings[i].position = new Vector3(z.X, 0.148f, z.Z);
                float pulse = 0.94f + 0.08f * Mathf.Sin(t * 12f + i);
                zoneRings[i].localScale = new Vector3(radius * pulse, 1f, radius * pulse);
                zoneRings[i].rotation = Quaternion.Euler(0f, t * 75f + i * 45f, 0f);
            }

            // 三同已经自动合成，不再为未选中单位创建旧版手动合成金圈。

            // 5. 平滑更新场景压暗（SceneDarken）
            float speed = targetDarken > currentDarken ? 14f : 7f;
            currentDarken = Mathf.MoveTowards(currentDarken, targetDarken, Mathf.Max(0.01f, unscaledDt) * speed);
            ApplyLightingDarken(currentDarken);
        }

        public void ResetDarkenImmediate()
        {
            targetDarken = 0f;
            currentDarken = 0f;
            ApplyLightingDarken(0f);
        }

        private void ApplyLightingDarken(float strength)
        {
            float factor = Mathf.Lerp(1f, 0.32f, Mathf.Clamp01(strength));
            if (mainSun != null) mainSun.intensity = BaseSunIntensity * factor;
            RenderSettings.ambientSkyColor = BaseSky * factor;
            RenderSettings.ambientEquatorColor = BaseEquator * factor;
            RenderSettings.ambientGroundColor = BaseGround * factor;
            Color bg = BaseBg * Mathf.Lerp(1f, 0.35f, strength);
            bg.a = 1f;
            if (mainCamera != null) mainCamera.backgroundColor = bg;
        }

        private static void ApplyMatColor(Material mat, Color col)
        {
            if (mat == null) return;
            mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        }

        private GameObject CreateRingObject(Transform parent, string objName, float diameter, Color color)
        {
            var go = new GameObject(objName, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(diameter, 1f, diameter);
            go.GetComponent<MeshFilter>().sharedMesh = ringMesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = CreateTransparentMat(objName + "_" + ColorUtility.ToHtmlStringRGBA(color), color);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private GameObject CreateFlatQuad(Transform parent, string objName, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = CreateTransparentMat(objName, color);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private Material CreateTransparentMat(string key, Color color)
        {
            if (mats.TryGetValue(key, out var existing) && existing != null) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(shader) { color = color };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            mats[key] = m;
            return m;
        }

        private void OnDestroy()
        {
            ResetDarkenImmediate();
            foreach (var kv in mats) if (kv.Value != null) Destroy(kv.Value);
            mats.Clear();
        }
    }
}
