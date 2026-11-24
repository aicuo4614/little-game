using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client
{
    /// <summary>三维表现只读取状态和事件，不计算伤害或胜负。</summary>
    public sealed class BattleSceneView : MonoBehaviour
    {
        private BattleApplication app;
        private readonly Dictionary<int, UnitVisual> visuals = new Dictionary<int, UnitVisual>();
        private CourtyardEnvironment environment;
        private readonly List<GameObject> effects = new List<GameObject>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private long sequence;
        private Mesh ringMesh;
        private Transform world, telegraph;
        public Camera Camera { get; private set; }
        public BattleApplication App => app;
        public void Build(BattleApplication application)
        {
            app = application; world = new GameObject("BattleWorld").transform; world.SetParent(transform);
            foreach (var old in FindObjectsOfType<Camera>()) old.enabled = false;
            Camera = new GameObject("CombatCamera").AddComponent<Camera>(); Camera.tag = "MainCamera"; Camera.transform.SetParent(world);
            Camera.orthographic = true; Camera.orthographicSize = 5.55f; Camera.rect = new Rect(0, .32f, 1, .49f); Camera.cullingMask = ~(1 << 5);
            Camera.transform.position = new Vector3(0, 11.5f, -8.8f); Camera.transform.LookAt(new Vector3(0, 0, .2f));
            Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.09f, .085f, .14f); Camera.nearClipPlane = .1f; Camera.farClipPlane = 60;
            foreach (var old in FindObjectsOfType<Light>()) old.enabled = false;
            var light = new GameObject("CombatSun").AddComponent<Light>(); light.transform.SetParent(world); light.type = LightType.Directional; light.intensity = 1.25f; light.transform.rotation = Quaternion.Euler(50, -35, 0); light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.64f,.70f,.80f);
            RenderSettings.ambientEquatorColor = new Color(.56f,.58f,.61f);
            RenderSettings.ambientGroundColor = new Color(.31f,.34f,.39f);
            light.color = new Color(1f,.96f,.88f); light.intensity = 1.15f;
            environment = new GameObject("RuinedCourtyard").AddComponent<CourtyardEnvironment>();
            environment.transform.SetParent(world,false); environment.Build();
            telegraph = Box("BossWarning", new Vector3(0, -.1f, 0), new Vector3(2.35f, .025f, 7.5f), new Color(.8f, .22f, .15f)).transform;
            telegraph.gameObject.SetActive(false);
        }
        private void LateUpdate()
        {
            if (app == null) return;
            var s = app.Session;
            environment.Apply(s.FencesUp, Time.unscaledDeltaTime);
            telegraph.gameObject.SetActive(s.Phase == RunPhase.Fighting && s.SlamLane >= 0);
            if (s.SlamLane >= 0) telegraph.position = new Vector3(BattleDefinitions.LaneX(s.SlamLane), .135f, -.9f);
            var ids = new HashSet<int>();
            foreach (var t in s.Troops.Where(t => t.Deployed))
            {
                ids.Add(t.Id); var v = Get(t.Id, (int)t.Kind, false, false);
                var motion = v.Model.GetComponent<CharacterMotion>();
                motion?.SetAlive(t.Alive || s.Phase != RunPhase.Fighting);
                v.Root.gameObject.SetActive(t.Alive || s.Phase != RunPhase.Fighting || motion != null && !motion.DeathFinished);
                v.Root.position = new Vector3(t.X, .11f, t.Z); v.Root.rotation = Quaternion.Euler(0, 0, 0);
                v.Name.text = new string('★', t.Star); v.Health.localScale = new Vector3(Mathf.Clamp01(t.Hp / t.MaxHp), 1, 1);
                v.Aura.SetActive(t.Shield > 0); v.Root.localScale = Vector3.one * (t.Star == 3 ? 1.18f : t.Star == 2 ? 1.08f : 1);
                v.Animate(s.Phase == RunPhase.Fighting, Time.unscaledTime); v.Model.GetComponent<CharacterMotion>()?.Apply(s.Paused || s.Aim != AimKind.None);
            }
            foreach (var e in s.Enemies)
            {
                ids.Add(e.Id); var v = Get(e.Id, (int)e.Kind, true, e.Boss);
                var motion = v.Model.GetComponent<CharacterMotion>(); motion?.SetAlive(e.Alive);
                v.Root.gameObject.SetActive(e.Alive || motion != null && !motion.DeathFinished); v.Root.position = new Vector3(e.X, .11f, e.Z); v.Root.rotation = Quaternion.Euler(0, 180, 0);
                v.Name.text = e.Boss ? BattleDefinitions.Name(e.Kind) : ""; v.Health.localScale = new Vector3(Mathf.Clamp01(e.Hp / e.MaxHp), 1, 1);
                v.Aura.SetActive(e.Alive && e.FrozenUntil > s.BattleTime); v.Animate(e.Alive && s.Phase == RunPhase.Fighting && e.FrozenUntil <= s.BattleTime, Time.unscaledTime); v.Model.GetComponent<CharacterMotion>()?.Apply(s.Paused || s.Aim != AimKind.None || e.Alive && e.FrozenUntil > s.BattleTime);
            }
            foreach (var id in visuals.Keys.Where(id => !ids.Contains(id)).ToArray()) { Destroy(visuals[id].Root.gameObject); visuals.Remove(id); }
            foreach (var ev in s.EventsAfter(sequence))
            {
                sequence = Math.Max(sequence, ev.Sequence);
                if (ev.Type == "skill" || ev.Type == "hit" || ev.Type == "freeze") Feedback(ev);
                if ((ev.Type == "skill" || ev.Type == "hit" || ev.Type == "troop_hit") && visuals.TryGetValue(ev.Source, out var actor)) actor.Model.GetComponent<CharacterMotion>()?.Attack();
                if ((ev.Type == "hit" || ev.Type == "troop_hit") && visuals.TryGetValue(ev.Target, out var injured)) injured.Model.GetComponent<CharacterMotion>()?.Hit();
                if (ev.Type == "heal" && visuals.TryGetValue(ev.Source, out var healer)) healer.Model.GetComponent<CharacterMotion>()?.Attack();
                if (ev.Type == "boss_slam" && visuals.TryGetValue(ev.Source, out var giant)) giant.Model.GetComponent<CharacterMotion>()?.Slam();
            }
            effects.RemoveAll(e => e == null);
        }
        private UnitVisual Get(int id, int kind, bool enemy, bool boss)
        {
            string visualKey = kind + "/" + enemy + "/" + boss;
            if (visuals.TryGetValue(id, out var found))
            {
                if (found.Key == visualKey) return found;
                found.Root.gameObject.SetActive(false); Destroy(found.Root.gameObject); visuals.Remove(id);
            }
            var root = new GameObject((enemy ? "Enemy_" : "Troop_") + id).transform; root.SetParent(world);
            var name = enemy ? (kind == 2 ? "Mage" : "Barbarian") : kind == 0 ? "Knight" : kind == 1 ? "Ranger" : "Mage";
            string role = enemy ? new[] { "Grunt", "Runner", "Armored", "Healer", "Giant" }[kind] : new[] { "Shield", "Archer", "Storm", "Frost" }[kind];
            var appearance = Resources.Load<UnitAppearance>("UnitAppearances/" + role);
            var prefab = appearance != null ? appearance.Model : Resources.Load<GameObject>("CombatCharacters/" + name);
            GameObject model;
            if (prefab != null)
            {
                model = Instantiate(prefab, root); model.name = appearance != null ? "Unit_" + role : "KayKit_" + name;
                var renderers = model.GetComponentsInChildren<Renderer>();
                if (appearance == null)
                {
                    var texture = Resources.Load<Texture2D>("CombatCharacters/" + name.ToLowerInvariant() + "_texture");
                    var material = Mat("KayKit_" + name, Color.white); material.mainTexture = texture;
                    foreach (var r in renderers) r.sharedMaterial = material;
                }
                // 骨架FBX以脚底和渲染包围盒归一化，避免不同导出单位导致大小失控。
                Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                float scale = (appearance != null ? appearance.Height : boss ? 2.5f : .92f) / Mathf.Max(.01f, bounds.size.y); model.transform.localScale *= scale;
                model.transform.localPosition = new Vector3(0, -bounds.min.y * scale, 0);
                var motion = model.AddComponent<CharacterMotion>();
                if (appearance != null) motion.Configure(appearance); else motion.Configure(kind);
            }
            else model = Primitive(PrimitiveType.Capsule, root, Vector3.up * .45f, new Vector3(.48f, .48f, .48f), enemy ? new Color(.65f, .27f, .23f) : ColorFor(kind));
            GroundRing(root, "FactionRing", boss ? 2.6f : .75f, boss ? 1.6f : .75f, enemy ? new Color(.65f, .24f, .19f) : ColorFor(kind));
            var aura = GroundRing(root, "StatusRing", .98f, .98f, new Color(.27f, .7f, .92f)); aura.SetActive(false);
            var label = Label(root, "", boss ? 2.82f : 1.35f);
            var healthRoot = new GameObject("Health").transform; healthRoot.SetParent(root); healthRoot.localPosition = new Vector3(0, boss ? 2.65f : 1.2f, 0); healthRoot.rotation = Camera.transform.rotation;
            Primitive(PrimitiveType.Cube, healthRoot, Vector3.zero, new Vector3(.7f, .065f, .03f), new Color(.2f, .24f, .24f));
            var health = new GameObject("Fill").transform; health.SetParent(healthRoot, false);
            Primitive(PrimitiveType.Cube, health, new Vector3(0, 0, -.02f), new Vector3(.68f, .05f, .03f), enemy ? new Color(.85f, .4f, .32f) : new Color(.39f, .8f, .59f));
            var visual = new UnitVisual { Key = visualKey, Root = root, Model = model.transform, Name = label, Health = health, Aura = aura, BaseY = model.transform.localPosition.y };
            visuals.Add(id, visual); return visual;
        }
        private void Feedback(CombatEvent e)
        {
            if (!visuals.TryGetValue(e.Source, out var source) || !visuals.TryGetValue(e.Target, out var target)) return;
            Color color = e.Type == "freeze" ? new Color(.35f, .8f, 1) : e.Type == "skill" ? new Color(.95f, .76f, .3f) : new Color(.9f, .86f, .65f);
            var go = new GameObject("SkillFeedback"); go.transform.SetParent(world); var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Mat("Line" + color, color, true); line.positionCount = 2; line.SetPosition(0, source.Root.position + Vector3.up * .65f); line.SetPosition(1, target.Root.position + Vector3.up * .65f); line.startWidth = line.endWidth = e.Type == "skill" ? .09f : .035f;
            effects.Add(go); Destroy(go, .15f);
        }
        public Vector2 UnitScreen(int id) => visuals.TryGetValue(id, out var v) ? (Vector2)Camera.WorldToScreenPoint(v.Root.position + Vector3.up * .4f) : new Vector2(-1000, -1000);
        public Vector2 CellScreen(int lane, int row) => Camera.WorldToScreenPoint(new Vector3(BattleDefinitions.LaneX(lane), 0, BattleDefinitions.RowZ(row)));
        public int HitUnit(Vector2 screen, bool enemy)
        {
            var s = app.Session;
            var ids = enemy ? s.Enemies.Where(e => e.Alive).Select(e => e.Id) : s.Troops.Where(t => t.Deployed).Select(t => t.Id);
            return ids.Select(id => new { id, d = Vector2.Distance(screen, UnitScreen(id)) }).Where(x => x.d < Mathf.Max(23, Screen.width * .065f)).OrderBy(x => x.d).Select(x => x.id).FirstOrDefault();
        }
        public bool HitCell(Vector2 screen, out int lane, out int row)
        {
            float best = float.MaxValue; lane = row = -1;
            for (int l = 0; l < 3; l++) for (int r = 0; r < 3; r++) { float d = Vector2.Distance(screen, CellScreen(l, r)); if (d < best) { best = d; lane = l; row = r; } }
            return best < Screen.width * .14f && Camera.pixelRect.Contains(screen);
        }
        public int HitLane(Vector2 screen)
        {
            var ray = Camera.ScreenPointToRay(screen); var plane = new Plane(Vector3.up, Vector3.zero);
            if (!Camera.pixelRect.Contains(screen) || !plane.Raycast(ray, out float enter)) return -1;
            float x = ray.GetPoint(enter).x; return Mathf.Abs(x) > 3.7f ? -1 : Mathf.Clamp(Mathf.RoundToInt(x / 2.5f) + 1, 0, 2);
        }
        private Material Mat(string key, Color color, bool unlit = false)
        {
            if (materials.TryGetValue(key, out var m)) return m;
            m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")); m.color = color; materials.Add(key, m); return m;
        }
        private GameObject Box(string name, Vector3 position, Vector3 scale, Color color) { var go = Primitive(PrimitiveType.Cube, world, position, scale, color); go.name = name; return go; }
        private GameObject Primitive(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
        { var go = GameObject.CreatePrimitive(type); go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = Mat(color.ToString(), color); var collider = go.GetComponent<Collider>(); if (collider != null) Destroy(collider); return go; }
        private GameObject GroundRing(Transform parent, string name, float width, float depth, Color color)
        {
            if (ringMesh == null)
            {
                const int segments = 40;
                var vertices = new Vector3[segments * 2]; var triangles = new int[segments * 6];
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2 / segments; var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    vertices[i * 2] = direction * .5f; vertices[i * 2 + 1] = direction * .44f;
                    int next = (i + 1) % segments; int t = i * 6;
                    triangles[t] = i * 2; triangles[t + 1] = i * 2 + 1; triangles[t + 2] = next * 2;
                    triangles[t + 3] = next * 2; triangles[t + 4] = i * 2 + 1; triangles[t + 5] = next * 2 + 1;
                }
                ringMesh = new Mesh { name = "Unit ground ring", vertices = vertices, triangles = triangles }; ringMesh.RecalculateNormals(); ringMesh.RecalculateBounds();
            }
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, .025f, 0); go.transform.localScale = new Vector3(width, 1, depth);
            go.GetComponent<MeshFilter>().sharedMesh = ringMesh;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = Mat("Ring" + color, color, true); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return go;
        }
        private TextMesh Label(Transform parent, string text, float y)
        {
            var go = new GameObject("Label"); go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(0, y, 0); go.transform.rotation = Camera.transform.rotation;
            var tm = go.AddComponent<TextMesh>(); tm.text = text; tm.fontSize = 32; tm.characterSize = .035f; tm.anchor = TextAnchor.MiddleCenter; tm.color = new Color(.96f, .97f, .9f);
            tm.font = Resources.Load<Font>("GameFont"); if (tm.font != null) go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material; return tm;
        }
        public static Color ColorFor(int kind) => kind == 0 ? new Color(.25f, .59f, .76f) : kind == 1 ? new Color(.28f, .64f, .4f) : kind == 2 ? new Color(.58f, .35f, .82f) : new Color(.32f, .77f, .87f);
        public object Snapshot() => new { environment = environment.Snapshot(), actors = visuals.Select(pair => new { id = pair.Key, model = pair.Value.Model.name, active = pair.Value.Root.gameObject.activeSelf, action = pair.Value.Model.GetComponent<CharacterMotion>()?.CurrentAction, actionTime = pair.Value.Model.GetComponent<CharacterMotion>()?.ActionTime, animationPlaying = pair.Value.Model.GetComponent<Animation>() != null && pair.Value.Model.GetComponent<Animation>().isPlaying, renderers = pair.Value.Model.GetComponentsInChildren<Renderer>().Select(r => new { type = r.GetType().Name, visible = r.isVisible, shaders = r.sharedMaterials.Select(m => m != null && m.shader != null ? m.shader.name : "missing"), supported = r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.isSupported) }), upperArm = pair.Value.Model.GetComponentsInChildren<Transform>().Where(t => t.name == "upperarm.l" || t.name == "upperarm_l" || t.name == "Body").Select(t => new { x = t.localEulerAngles.x, y = t.localEulerAngles.y, z = t.localEulerAngles.z }) }) };
        private void OnDestroy() { foreach (var m in materials.Values) Destroy(m); if (ringMesh != null) Destroy(ringMesh); }
        private sealed class UnitVisual
        {
            public string Key; public Transform Root, Model, Health; public TextMesh Name; public GameObject Aura; public float BaseY;
            public void Animate(bool fighting, float time) { var p = Model.localPosition; p.y = BaseY + (fighting ? Mathf.Sin(time * 5 + Root.GetInstanceID()) * .025f : 0); Model.localPosition = p; }
        }
    }
}
