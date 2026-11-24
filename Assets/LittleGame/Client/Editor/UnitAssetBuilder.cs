using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LittleGame.Client.Editor
{
    /// <summary>将外部样品整理为本项目资源；不修改源工程。</summary>
    public static class UnitAssetBuilder
    {
        public const string Source = "Assets/LittleGame/Art/UnitSources";
        private const string Generated = "Assets/LittleGame/Art/UnitGenerated";

        [MenuItem("Little Game/Combat/Build Unit Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出Play");
            foreach (var folder in new[] { Generated + "/Materials", Generated + "/Clips", "Assets/LittleGame/Resources/UnitAppearances", "Assets/LittleGame/Resources/UnitModels" }) Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Roco/Character");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Demo卡通Shader尚未就绪");
            var rows = new System.Text.StringBuilder();
            BuildHero("Shield", 19, "SwordAndShield", new Color(.4f, .7f, 1), rows);
            BuildHero("Archer", 11, "BowAndArrow", new Color(.08f, .28f, .12f), rows);
            BuildHero("Storm", 7, "MagicWand", new Color(.2f, .07f, .35f), rows);
            BuildHero("Frost", 7, "MagicWand", new Color(.4f, .68f, .8f), rows);
            BuildHero("Grunt", 2, "SwordAndShield", new Color(.8f, .35f, .24f), rows);
            BuildHero("Armored", 20, "SwordAndShield", new Color(.7f, .24f, .2f), rows);
            BuildHero("Healer", 14, "MagicWand", new Color(.9f, .4f, .3f), rows);
            BuildCreature("Runner", "Wolf/Wolf.FBX", "Wolf/Wolf.psd", .75f,
                new[] { "Wolf/Wolf@Idle.FBX", "Wolf/Wolf@Run Forward In Place.FBX", "Wolf/Wolf@Bite Attack.FBX", "Wolf/Wolf@Take Damage.FBX", "Wolf/Wolf@Die.FBX", "Wolf/Wolf@Bite Attack.FBX" }, rows);
            BuildCreature("Giant", "Golem/Attack01_Golem_Anim.fbx", "Golem/PolyartDefaultTex.png", 2.45f,
                new[] { "Golem/IdleBattle_Golem_Anim.fbx", "Golem/Walk_Golem_Anim.fbx", "Golem/Attack01_Golem_Anim.fbx", "Golem/GetHit_Golem_Anim.fbx", "Golem/Die_Golem_Anim.fbx", "Golem/Attack02_Golem_Anim.fbx" }, rows);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/LittleGame/Rendering/MobileRenderer.asset");
            if (!renderer.rendererFeatures.Any(f => f is RocoCharacterOutlineFeature))
            {
                var feature = ScriptableObject.CreateInstance<RocoCharacterOutlineFeature>(); feature.name = "UnitToonOutline";
                AssetDatabase.AddObjectToAsset(feature, renderer); renderer.rendererFeatures.Add(feature); EditorUtility.SetDirty(renderer);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Reports/Art"); File.WriteAllText("Reports/Art/unit-build-validation.txt", rows.ToString());
        }

        private static void BuildHero(string role, int sample, string style, Color accent, System.Text.StringBuilder report)
        {
            string path = Source + "/TinyHero/Prefab/ModularCharacters/MC" + sample.ToString("00") + ".prefab";
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); go.name = role;
            // 保留作者绑定好的骨架与武器位置，只调整实际选装件。
            foreach (var node in go.GetComponentsInChildren<Transform>(true))
                if (node.name.StartsWith("AC", StringComparison.Ordinal) || node.name.StartsWith("BackPack", StringComparison.Ordinal)) node.gameObject.SetActive(false);
            if (role == "Archer") { Select(go, "Body", "Body08"); Select(go, "Hat", "Hat05"); }
            if (role == "Storm" || role == "Frost")
            {
                Select(go, "Hat", role == "Storm" ? "Hat13" : "Hat08");
                Select(go, "Wand", role == "Storm" ? "Wand02" : "Wand06");
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Source + "/TinyHero/Texture/DefaultPBR/Albedo.png");
            var baseMaterial = ToonMaterial("TinyHero", texture, Color.white);
            var accentMaterial = ToonMaterial(role + "Accent", null, accent);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials = Enumerable.Repeat(baseMaterial, r.sharedMaterials.Length).ToArray();
                if (r.name.StartsWith("Hat") || ((role == "Storm" || role == "Frost") && r.name.StartsWith("Wand")))
                    r.sharedMaterials = Enumerable.Repeat(accentMaterial, r.sharedMaterials.Length).ToArray();
            }
            string suffix = style == "SwordAndShield" ? "SwordAndShiled" : style;
            var profile = PrepareProfile(role, go, path, 1.1f);
            string dir = "TinyHero/Animation/" + style + "/";
            AssignClips(profile, go, new[] { dir + "Idle_Battle_" + suffix + ".fbx", dir + "InPlace/MoveFWD_Battle_InPlace_" + style + ".fbx", dir + "Attack01_" + suffix + ".fbx", dir + "GetHit01_" + style + ".fbx", dir + "Die01_" + style + ".fbx", dir + "Attack01_" + suffix + ".fbx" }, report);
            Finish(profile, go, report);
        }
        private static void Select(GameObject go, string prefix, string selected)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix, StringComparison.Ordinal) && (prefix != "Body" || t.name.Length > 4 && char.IsDigit(t.name[4]))) t.gameObject.SetActive(t.name == selected);
        }
        private static void BuildCreature(string role, string model, string texture, float height, string[] clips, System.Text.StringBuilder report)
        {
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/" + model)); go.name = role;
            var material = ToonMaterial(role, AssetDatabase.LoadAssetAtPath<Texture2D>(Source + "/" + texture), Color.white);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
            var profile = PrepareProfile(role, go, Source + "/" + model, height);
            AssignClips(profile, go, clips, report); Finish(profile, go, report);
        }
        private static UnitAppearance PrepareProfile(string role, GameObject go, string source, float height)
        {
            foreach (var component in go.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in go.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(component);
            // 剔除未选配件，避免每个单位实例携带整套隐藏头饰与武器。
            foreach (var t in go.GetComponentsInChildren<Transform>(true).Reverse().ToArray())
                if (t != null && t != go.transform && !t.gameObject.activeSelf) UnityEngine.Object.DestroyImmediate(t.gameObject);
            string path = "Assets/LittleGame/Resources/UnitAppearances/" + role + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<UnitAppearance>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<UnitAppearance>(); AssetDatabase.CreateAsset(profile, path); }
            profile.name = role; profile.Source = source; profile.Height = height; return profile;
        }
        private static void AssignClips(UnitAppearance profile, GameObject go, string[] files, System.Text.StringBuilder report)
        {
            var clips = new AnimationClip[files.Length];
            string[] labels = { "Idle", "Move", "Attack", "Hit", "Death", "Slam" };
            for (int i = 0; i < files.Length; i++)
            {
                var source = AssetDatabase.LoadAllAssetsAtPath(Source + "/" + files[i]).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview", StringComparison.Ordinal));
                if (source == null) throw new InvalidOperationException("缺动作 " + files[i]);
                var clip = UnityEngine.Object.Instantiate(source); clip.legacy = true; clip.name = profile.name + "_" + labels[i];
                AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                var missing = AnimationUtility.GetCurveBindings(clip).Where(b => !string.IsNullOrEmpty(b.path) && go.transform.Find(b.path) == null).ToArray();
                report.AppendLine(profile.name + " " + labels[i] + " curves=" + AnimationUtility.GetCurveBindings(clip).Length + " missing=" + missing.Length + " paths=" + string.Join(",", missing.Take(3).Select(b => b.path)));
                if (missing.Length > 0) throw new InvalidOperationException(profile.name + "动作骨架路径不匹配：" + missing[0].path);
                string path = Generated + "/Clips/" + clip.name + ".anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing == null) AssetDatabase.CreateAsset(clip, path);
                else { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); clip = existing; }
                clips[i] = clip;
            }
            profile.Idle = clips[0]; profile.Move = clips[1]; profile.Attack = clips[2]; profile.Hit = clips[3]; profile.Death = clips[4]; profile.Slam = clips[5];
        }
        private static void Finish(UnitAppearance profile, GameObject go, System.Text.StringBuilder report)
        {
            var animation = go.AddComponent<Animation>(); animation.clip = profile.Idle; animation.AddClip(profile.Idle, "Idle");
            animation.Play("Idle"); animation.Sample();
            var renderers = go.GetComponentsInChildren<Renderer>();
            int triangles = renderers.Sum(r => TriangleCount(r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh));
            report.AppendLine(profile.name + " renderers=" + renderers.Length + " triangles=" + triangles + " nodes=" + go.GetComponentsInChildren<Transform>().Length);
            profile.Model = PrefabUtility.SaveAsPrefabAsset(go, "Assets/LittleGame/Resources/UnitModels/" + profile.name + ".prefab");
            EditorUtility.SetDirty(profile); UnityEngine.Object.DestroyImmediate(go);
        }
        private static int TriangleCount(Mesh mesh) { if (mesh == null) return 0; int count = 0; for (int i = 0; i < mesh.subMeshCount; i++) count += (int)mesh.GetIndexCount(i) / 3; return count; }
        private static Material ToonMaterial(string name, Texture texture, Color color)
        {
            string path = Generated + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Roco/Character")); AssetDatabase.CreateAsset(material, path); }
            material.shader = Shader.Find("Roco/Character"); material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", color);
            material.SetFloat("_DyeStrength", 0); material.SetFloat("_AOStrength", 0);
            material.SetFloat("_ToonLit", 1); material.SetFloat("_ToonMid", .64f); material.SetFloat("_ToonShadow", .32f); material.SetFloat("_AmbientStrength", .55f);
            material.SetFloat("_OutlineUseSmoothNormal", 0); material.SetFloat("_OutlineWidthMode", 1); material.SetFloat("_OutlineWidth", .8f);
            material.SetFloat("_ReceiveShadows", 0); material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            EditorUtility.SetDirty(material); return material;
        }

        [MenuItem("Little Game/Combat/Inspect Unit Sources")]
        public static void Inspect()
        {
            Directory.CreateDirectory("Reports/Art");
            var report = new System.Text.StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { Source + "/TinyHero/Prefab/ModularCharacters" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var go = UnityEngine.Object.Instantiate(prefab);
                report.AppendLine(path + " active=" + string.Join(",", go.GetComponentsInChildren<Renderer>().Select(r => r.name)));
                UnityEngine.Object.DestroyImmediate(go);
            }
            foreach (var path in Directory.GetFiles(Source, "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)))
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (clip != null) report.AppendLine(path + " clip=" + clip.name + " length=" + clip.length + " bindings=" + string.Join(",", AnimationUtility.GetCurveBindings(clip).Take(3).Select(b => b.path)));
            }
            File.WriteAllText("Reports/Art/unit-import-inspection.txt", report.ToString());
        }

        [MenuItem("Little Game/Combat/Preview Unit Sources")]
        public static void Preview()
        {
            Directory.CreateDirectory("Reports/Art/Thumbnails");
            var preview = new PreviewRenderUtility();
            try
            {
                preview.camera.fieldOfView = 30;
                preview.camera.backgroundColor = new Color(.16f, .2f, .24f);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40, 30, 0);
                preview.lights[1].intensity = .6f;
                foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { Source + "/TinyHero/Prefab/ModularCharacters" }))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    var go = UnityEngine.Object.Instantiate(prefab);
                    foreach (var animator in go.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
                    var rs = go.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) { UnityEngine.Object.DestroyImmediate(go); continue; }
                    var bounds = rs[0].bounds; foreach (var r in rs) bounds.Encapsulate(r.bounds);
                    preview.AddSingleGO(go);
                    float h = Mathf.Max(.1f, bounds.size.y);
                    preview.camera.transform.position = bounds.center + new Vector3(h * .85f, h * .5f, h * 2.5f);
                    preview.camera.transform.LookAt(bounds.center);
                    preview.camera.nearClipPlane = h * .01f; preview.camera.farClipPlane = h * 10;
                    preview.BeginStaticPreview(new Rect(0, 0, 256, 256));
                    preview.Render(true);
                    var texture = preview.EndStaticPreview();
                    File.WriteAllBytes("Reports/Art/Thumbnails/" + prefab.name + ".png", texture.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(go);
                }
            }
            finally { preview.Cleanup(); }
        }
    }

    internal sealed class UnitSourceImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(UnitAssetBuilder.Source + "/", StringComparison.Ordinal)) return;
            var model = (ModelImporter)assetImporter;
            model.animationType = ModelImporterAnimationType.Legacy;
            model.importAnimation = assetPath.Contains("/Animation/") || assetPath.Contains("/Wolf/") || assetPath.Contains("/Golem/");
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.importCameras = model.importLights = false;
            model.isReadable = false;
        }
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UnitAssetBuilder.Source + "/", StringComparison.Ordinal)) return;
            var texture = (TextureImporter)assetImporter;
            texture.maxTextureSize = 1024;
            texture.mipmapEnabled = true;
        }
    }
}
