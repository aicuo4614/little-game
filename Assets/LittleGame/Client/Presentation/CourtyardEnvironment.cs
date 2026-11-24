using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client
{
    /// <summary>
    /// 无限横向遗迹长道（无城墙、无三路栅栏，参考多层视差滚屏）：
    /// 1. 近中景 3 块主道路 Chunk（各宽 10.4m）无缝向 -X 循环滚动，铺石路面、北侧遗迹廊柱与拱门绵延不绝；
    /// 2. 远景视差层 3 块 Chunk 以 0.45 倍速滚动（古树、远山石柱、残岩），增强 3D 远征行军速度感；
    /// 3. 3×3 符文阵位角标固定锚定在左侧世界坐标（静态合批），军团边跑边布阵时拖拽网格丝毫不漂移。
    /// </summary>
    public sealed class CourtyardEnvironment : MonoBehaviour
    {
        private const float ChunkWidth = 10.4f;
        private const int ChunkCount = 3;
        private const float FarParallaxFactor = 0.45f;

        private readonly List<Transform> roadChunks = new List<Transform>();
        private readonly List<Transform> farChunks = new List<Transform>();
        private readonly List<Material> materials = new List<Material>();
        private Material stone, pale, dark, earth, moss, blue, floorA, floorB;
        private float currentScrollSpeed;
        private float scrollTotal;
        private int modelInstances, missingModels;

        public float CurrentScrollSpeed => currentScrollSpeed;

        public void Build()
        {
            stone = Material("Weathered limestone", new Color(.44f, .49f, .51f));
            pale = Material("Carved capstone", new Color(.74f, .73f, .68f));
            dark = Material("Recessed stone", new Color(.23f, .29f, .32f));
            earth = Material("Roadbed earth", new Color(.22f, .28f, .25f));
            moss = Material("Muted vegetation", new Color(.26f, .43f, .29f));
            blue = Material("Rune inlay", new Color(.28f, .65f, .78f));
            floorA = Material("Paving ash", new Color(.54f, .57f, .58f));
            floorB = Material("Paving variation", new Color(.50f, .53f, .55f));

            // 1. 静态底层路基（全面去除蓝色 3×3 网格小框，保持干净开阔的大道画面）
            var staticRoot = new GameObject("StaticRoadbedLayer").transform;
            staticRoot.SetParent(transform, false);
            Cube(staticRoot, "ContinuousRoadbed", new Vector3(0f, -.52f, 1.4f), new Vector3(44f, .92f, 14.5f), earth);
            Cube(staticRoot, "NorthCurbBase", new Vector3(0f, -.05f, 3.65f), new Vector3(44f, .18f, .6f), dark);
            Cube(staticRoot, "SouthCurbBase", new Vector3(0f, -.05f, -3.55f), new Vector3(44f, .18f, .55f), dark);
            StaticBatchingUtility.Combine(staticRoot.gameObject);

            // 2. 近中景无限循环路段 Chunk（先在原点构建确保模型包围盒无偏移，再排布至 [-10.4, 0, +10.4]）
            for (int c = 0; c < ChunkCount; c++)
            {
                var chunk = new GameObject("RoadChunk_" + c).transform;
                chunk.SetParent(transform, false);
                chunk.localPosition = Vector3.zero;
                BuildRoadChunk(chunk, c);
                chunk.localPosition = new Vector3((c - 1) * ChunkWidth, 0f, 0f);
                roadChunks.Add(chunk);
            }

            // 3. 远景视差循环层（0.45 倍速，营造广阔遗迹纵深感）
            for (int c = 0; c < ChunkCount; c++)
            {
                var far = new GameObject("FarParallaxChunk_" + c).transform;
                far.SetParent(transform, false);
                far.localPosition = Vector3.zero;
                BuildFarChunk(far, c);
                far.localPosition = new Vector3((c - 1) * ChunkWidth, 0f, 0f);
                farChunks.Add(far);
            }
        }

        private void BuildRoadChunk(Transform parent, int variant)
        {
            // 宽阔无隔栏石板大道（10 列 × 6 行地砖，覆盖 Z ∈ [-3.12, +3.12]）
            for (int xi = 0; xi < 10; xi++)
            {
                float x = (xi - 4.5f) * 1.04f;
                for (int zi = 0; zi < 6; zi++)
                {
                    float z = (zi - 2.5f) * 1.04f;
                    Place(parent, "brick_" + (1 + (xi + zi + variant) % 4),
                        new Vector3(x, 0f, z),
                        new Vector3(1.035f, .10f, 1.035f),
                        (xi + zi) % 3 == 0 ? floorA : floorB, true);
                }
            }

            // 北侧遗迹长廊：石墙、立柱、发光水晶、装饰岩石与每段一座古拱门
            for (int i = 0; i < 4; i++)
            {
                float px = -3.9f + i * 2.6f;
                Place(parent, "pillar_narrow_1", new Vector3(px, 0f, 3.85f), new Vector3(.75f, i == 1 ? 2.15f : 1.45f, .75f), pale);
                if (i < 3)
                    Place(parent, "wall_element_tileable_4meters_top", new Vector3(px + 1.3f, 0f, 3.92f), new Vector3(2.25f, .62f, .36f), stone, true);
                Place(parent, "nw_Grass", new Vector3(px + .65f, .05f, 3.38f), new Vector3(.45f, .36f, .45f), moss);
                if ((i + variant) % 2 == 0)
                    Place(parent, "Rock_1", new Vector3(px - .45f, .04f, 3.55f), new Vector3(.62f, .42f, .62f), dark);
            }

            // 标志性古遗迹拱门（位于道路北侧作为推进参照物，不遮挡角色）
            Place(parent, "archway", new Vector3(0.4f, 0f, 4.45f), new Vector3(4.2f, 2.85f, .9f), pale, true);
            Place(parent, "pillar", new Vector3(-4.5f, 0f, 4.35f), new Vector3(.88f, 2.0f, .88f), pale);
            Place(parent, "Crystal_2", new Vector3(-4.5f, 2.05f, 4.35f), new Vector3(.34f, .62f, .34f), blue);

            // 南侧低矮石沿与点缀草丛（严格压低高度，绝不遮挡底行单位）
            for (int i = 0; i < 5; i++)
            {
                float sx = -4.2f + i * 2.1f;
                Place(parent, "brick_3", new Vector3(sx, .05f, -3.55f), new Vector3(1.1f, .20f, .35f), stone, true);
                if ((i + variant) % 2 == 1)
                    Place(parent, "nw_Grass", new Vector3(sx + .7f, .04f, -3.45f), new Vector3(.38f, .24f, .38f), moss);
            }
        }

        private void BuildFarChunk(Transform parent, int variant)
        {
            // 远景森林与遗迹高柱（营造 3D 纵深与多层次视差滚动）
            for (int i = 0; i < 3; i++)
            {
                float fx = -3.4f + i * 3.4f + (variant % 2) * 0.5f;
                Place(parent, "Tree_04", new Vector3(fx, -.12f, 6.25f + (i % 2) * 0.85f), new Vector3(1.65f, 2.15f, 1.65f), moss);
                Place(parent, "pillar", new Vector3(fx + 1.55f, -.1f, 7.1f), new Vector3(1.05f, 2.6f + (i % 2) * 0.5f, 1.05f), dark);
            }
            Place(parent, "Rock_1", new Vector3(-1.2f, -.08f, 5.75f), new Vector3(1.25f, .75f, 1.0f), dark);
            Place(parent, "Rock_1", new Vector3(3.1f, -.08f, 5.95f), new Vector3(1.05f, .65f, .95f), stone);
        }

        /// <summary>按当前行军速度与相机横移量驱动无限横向卷轴与远景视差（参考 bump MapControl）。</summary>
        public void Apply(float scrollSpeed, float cameraDeltaX, float cameraCenterX, float dt)
        {
            currentScrollSpeed = Mathf.Max(0f, scrollSpeed);
            float roadMove = currentScrollSpeed * Mathf.Max(0f, dt);
            scrollTotal += roadMove;
            // 道路随行军向 -X 流动；远景按 0.45x 视差同时响应行军滚动与相机跟随偏移
            ScrollGroup(roadChunks, roadMove, cameraCenterX);
            float farMove = roadMove * FarParallaxFactor - cameraDeltaX * (1f - FarParallaxFactor);
            ScrollGroup(farChunks, farMove, cameraCenterX);
        }

        private static void ScrollGroup(List<Transform> chunks, float deltaX, float centerX)
        {
            float span = ChunkWidth * ChunkCount;
            float minX = centerX - span * 0.5f;
            float maxX = centerX + span * 0.5f;
            for (int i = 0; i < chunks.Count; i++)
            {
                var p = chunks[i].localPosition;
                p.x -= deltaX;
                while (p.x < minX) p.x += span;
                while (p.x > maxX) p.x -= span;
                chunks[i].localPosition = p;
            }
        }

        public object Snapshot() => new
        {
            theme = "infinite-ruins-road-horizontal",
            infiniteScrolling = true,
            chunkCount = roadChunks.Count,
            scrollSpeed = Math.Round(currentScrollSpeed, 2),
            scrollOffset = Math.Round(scrollTotal, 2),
            modelInstances,
            missingModels,
            shader = stone.shader.name,
            shaderSupported = stone.shader.isSupported,
            renderers = GetComponentsInChildren<Renderer>().Length,
            staticBatchedRenderers = GetComponentsInChildren<Renderer>().Count(r => r.isPartOfStaticBatch),
            colliders = GetComponentsInChildren<Collider>().Length,
            missingMaterials = GetComponentsInChildren<Renderer>().Count(r => r.sharedMaterials.Any(m => m == null || m.shader == null || !m.shader.isSupported)),
            note = "无限横向循环遗迹大道（含远景视差），无城墙无栅栏，不添加玩法碰撞"
        };

        private Material Material(string name, Color color)
        {
            var shader = Resources.Load<Shader>("CourtyardSceneShader");
            if (shader == null) shader = Shader.Find("Scene/Simple");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Demo Scene/Simple Shader未就绪");
            var mat = new Material(shader) { name = name };
            mat.SetColor("_Color", color);
            mat.enableInstancing = true;
            materials.Add(mat);
            return mat;
        }

        private Transform Place(Transform parent, string asset, Vector3 position, Vector3 size, Material material, bool stretch = false)
        {
            var prefab = Resources.Load<GameObject>("Courtyard/" + asset);
            if (prefab == null) { missingModels++; throw new InvalidOperationException("场景模型缺失：" + asset); }
            var go = Instantiate(prefab, parent);
            go.name = asset;
            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var scale = new Vector3(
                size.x / Mathf.Max(.001f, bounds.size.x),
                size.y / Mathf.Max(.001f, bounds.size.y),
                size.z / Mathf.Max(.001f, bounds.size.z));
            if (!stretch) scale = Vector3.one * Mathf.Min(scale.x, scale.y, scale.z);
            t.localScale = Vector3.Scale(t.localScale, scale);
            t.localPosition = position - new Vector3(bounds.center.x * scale.x, bounds.min.y * scale.y, bounds.center.z * scale.z);
            foreach (var r in renderers)
            {
                r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            modelInstances++;
            return t;
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        private void OnDestroy()
        {
            foreach (var m in materials) Destroy(m);
        }
    }
}
