using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LittleGame.Client.Editor
{
    /// <summary>
    /// 将外部 3D 角色样品构建为标准 3D Animator + UnitMono 资源：
    /// 1. 使用 Generic 骨架导入（彻底淘汰 Legacy Animation）；
    /// 2. 为每个角色生成带 Idle / Walk / Attack / Hit / Death / Slam 状态的 AnimatorController；
    /// 3. 挂接 HotUpdate_UnitCore 的 UnitMono 与标准挂点（Root / BodyCenter / Head / LeftHand / RightHand / Spine / Custom1）；
    /// 4. 模型与外观统一产出至 Package_Unit，避免主包资源引用热更脚本。
    /// </summary>
    public static class UnitAssetBuilder
    {
        public const string Source = "Assets/LittleGame/Art/UnitSources";
        private const string Generated = "Assets/LittleGame/Art/UnitGenerated";
        public const string PackageUnitRoot = "Assets/Package_Unit/Low/Base";

        [MenuItem("Little Game/Combat/Build Unit Assets")]
        public static void Build() => Build(enemyMonstersOnly: false);

        [MenuItem("Little Game/Combat/Build Enemy Monster Assets")]
        public static void BuildEnemyMonsters() => Build(enemyMonstersOnly: true);

        [MenuItem("Little Game/Combat/Build KayKit Heroes")]
        public static void BuildKayKitHeroes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出 Play");
            var rows = new System.Text.StringBuilder();
            foreach (var folder in new[] { Generated + "/Materials", Generated + "/Clips", Generated + "/Controllers", "Assets/Package_Unit/Appearance/Base" })
                Directory.CreateDirectory(folder);
            BuildKayKitSet(rows);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Directory.CreateDirectory("Reports/Art");
            File.WriteAllText("Reports/Art/kaykit-hero-build.txt", rows.ToString());
        }

        private static void BuildKayKitSet(System.Text.StringBuilder rows)
        {
            BuildKayKit("Knight", "Melee_1H_Attack_Slice_Horizontal", false, rows);
            BuildKayKit("Ranger", "Ranged_Bow_Release", true, rows);
            BuildKayKit("Mage", "Ranged_Magic_Shoot", true, rows);
            BuildKayKit("Rogue", "Melee_1H_Attack_Slice_Diagonal", false, rows);
            BuildKayKit("Skeleton_Warrior", "Melee_1H_Attack_Slice_Horizontal", false, rows);
            BuildKayKit("Skeleton_Minion", "Ranged_2H_Shoot", true, rows);
            BuildKayKit("Skeleton_Mage", "Ranged_Magic_Shoot", true, rows);
            BuildKayKit("Skeleton_Rogue", "Melee_1H_Attack_Slice_Diagonal", false, rows);
        }

        private static void BuildKayKit(string role, string attack, bool ranged, System.Text.StringBuilder report)
        {
            bool skeleton = role.StartsWith("Skeleton_", StringComparison.Ordinal);
            string folder = Source + "/KayKit/" + (skeleton ? "Skeletons" : "Adventurers");
            string modelPath = folder + "/" + role + ".fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (prefab == null) throw new InvalidOperationException("缺 KayKit 模型：" + modelPath);
            var go = UnityEngine.Object.Instantiate(prefab);
            go.name = role;
            try
            {
                var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
                string texture = skeleton ? "skeleton_texture.png" : role.ToLowerInvariant() + "_texture.png";
                var material = ToonMaterial(role, AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + texture), Color.white);
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();

                if (role == "Skeleton_Minion")
                {
                    var hand = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t =>
                        t.name.Equals("hand_r", StringComparison.OrdinalIgnoreCase) ||
                        t.name.IndexOf("right_hand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.name.IndexOf("hand.R", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (hand == null) throw new InvalidOperationException("骷髅小兵缺右手武器挂点");
                    var crossbow = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/Skeleton_Crossbow.fbx"), hand);
                    crossbow.name = "EquippedCrossbow";
                    crossbow.transform.localPosition = Vector3.zero;
                    crossbow.transform.localRotation = Quaternion.identity;
                    foreach (var renderer in crossbow.GetComponentsInChildren<Renderer>(true))
                        renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    report.AppendLine(role + " weapon=" + hand.name + "/EquippedCrossbow");
                }

                var profile = PrepareProfile(role, go, modelPath, 1.18f);
                string prefix = "KayKit/Animation/Rig_Medium_";
                string action = prefix + (ranged ? "CombatRanged" : "CombatMelee") + ".fbx";
                string general = prefix + "General.fbx";
                AssignClips(profile, go, new[] { general, prefix + "MovementBasic.fbx", action, general, general, action }, report,
                    new[] { "Idle_A", "Running_A", attack, "Hit_A", "Death_A", attack });
                Finish(profile, go, avatar, report);
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void Build(bool enemyMonstersOnly)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出Play");

            foreach (var folder in new[]
            {
                Generated + "/Materials",
                Generated + "/Clips",
                Generated + "/Controllers",
                "Assets/Package_Unit/Appearance/Base",
                PackageUnitRoot
            })
            {
                Directory.CreateDirectory(folder);
            }

            // 强制将 UnitSources 下全部 FBX 切为 Generic 并重导入，确保生成非 Legacy 片段与 Avatar。
            ReimportUnitSourcesAsGeneric();
            AssetDatabase.Refresh();

            var shader = Shader.Find("Roco/Character");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Demo卡通Shader尚未就绪");

            var rows = new System.Text.StringBuilder();
            BuildEnemyMonsters(rows);
            if (!enemyMonstersOnly)
            {
                BuildKayKitSet(rows);
                BuildHero("Shield", 19, "SwordAndShield", new Color(.4f, .7f, 1), rows);
                BuildHero("Archer", 11, "BowAndArrow", new Color(.08f, .28f, .12f), rows);
                BuildHero("Storm", 7, "MagicWand", new Color(.2f, .07f, .35f), rows);
                BuildHero("Frost", 7, "MagicWand", new Color(.4f, .68f, .8f), rows);
                BuildCreature("Giant", "Golem/Attack01_Golem_Anim.fbx", "Golem/PolyartDefaultTex.png", 2.45f,
                    new[] { "Golem/IdleBattle_Golem_Anim.fbx", "Golem/Walk_Golem_Anim.fbx", "Golem/Attack01_Golem_Anim.fbx", "Golem/GetHit_Golem_Anim.fbx", "Golem/Die_Golem_Anim.fbx", "Golem/Attack02_Golem_Anim.fbx" }, rows);
            }

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/LittleGame/Rendering/MobileRenderer.asset");
            if (renderer != null)
            {
                if (!renderer.rendererFeatures.Any(f => f is RocoCharacterOutlineFeature))
                {
                    var feature = ScriptableObject.CreateInstance<RocoCharacterOutlineFeature>();
                    feature.name = "UnitToonOutline";
                    AssetDatabase.AddObjectToAsset(feature, renderer);
                    renderer.rendererFeatures.Add(feature);
                    EditorUtility.SetDirty(renderer);
                }

                if (!renderer.rendererFeatures.Any(f => f is SceneDarkenFeature))
                {
                    var darken = ScriptableObject.CreateInstance<SceneDarkenFeature>();
                    darken.name = "SceneDarken";
                    AssetDatabase.AddObjectToAsset(darken, renderer);
                    renderer.rendererFeatures.Add(darken);
                    EditorUtility.SetDirty(renderer);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Directory.CreateDirectory("Reports/Art");
            string report = enemyMonstersOnly ? "enemy-monster-build-validation.txt" : "unit-build-validation.txt";
            File.WriteAllText("Reports/Art/" + report, rows.ToString());
        }

        private static void BuildEnemyMonsters(System.Text.StringBuilder rows)
        {
            BuildCuteMonster("Grunt", "CuteMonsters/Werewolf/Werewolf", 1.20f, "Run Forward In Place", "Right Slash Attack", rows);
            BuildCuteMonster("Runner", "Wolf/Wolf", 1.00f, "Run Forward In Place", "Bite Attack", rows);
            BuildCuteMonster("Armored", "CuteMonsters/Hermit King/Hermit King", 1.25f, "Walk Forward In Place", "Slash Attack", rows);
            BuildCuteMonster("Healer", "CuteMonsters/Sunflower Fairy/Sunflower Fairy", 1.10f, "Fly Forward In Place", "Cast Spell 01", rows);
        }

        private static void BuildCuteMonster(string role, string source, float height, string move, string attack, System.Text.StringBuilder rows)
        {
            string[] actions = { "Idle", move, attack, "Take Damage", "Die", attack };
            BuildCreature(role, source + ".FBX", source + ".psd", height,
                actions.Select(action => source + "@" + action + ".FBX").ToArray(), rows);
        }

        internal static bool HasUnitAnimations(string path) => path.Contains("/Animation/")
            || path.Contains("/Wolf/") || path.Contains("/Golem/") || path.Contains("/CuteMonsters/");

        private static void ReimportUnitSourcesAsGeneric()
        {
            if (!Directory.Exists(Source)) return;
            var fbxs = Directory.GetFiles(Source, "*.fbx", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/'))
                .Where(HasUnitAnimations)
                .ToArray();
            var dirty = new List<string>();
            for (int i = 0; i < fbxs.Length; i++)
            {
                var importer = AssetImporter.GetAtPath(fbxs[i]) as ModelImporter;
                if (importer == null) continue;
                bool needImport = importer.animationType != ModelImporterAnimationType.Generic
                    || importer.avatarSetup == ModelImporterAvatarSetup.NoAvatar || !importer.importAnimation;
                if (needImport)
                {
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.importAnimation = true;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.isReadable = false;
                    EditorUtility.SetDirty(importer);
                    dirty.Add(fbxs[i]);
                }
            }
            if (dirty.Count == 0) return;
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < dirty.Count; i++)
                    AssetDatabase.WriteImportSettingsIfDirty(dirty[i]);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        private static void BuildHero(string role, int sample, string style, Color accent, System.Text.StringBuilder report)
        {
            string path = Source + "/TinyHero/Prefab/ModularCharacters/MC" + sample.ToString("00") + ".prefab";
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            go.name = role;

            var sourceAvatar = go.GetComponentInChildren<Animator>(true)?.avatar;
            if (sourceAvatar == null)
            {
                sourceAvatar = FindAnyAvatarInFolder(Source + "/TinyHero");
            }

            foreach (var node in go.GetComponentsInChildren<Transform>(true))
                if (node.name.StartsWith("AC", StringComparison.Ordinal) || node.name.StartsWith("BackPack", StringComparison.Ordinal))
                    node.gameObject.SetActive(false);

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
            AssignClips(profile, go, new[]
            {
                dir + "Idle_Battle_" + suffix + ".fbx",
                dir + "InPlace/MoveFWD_Battle_InPlace_" + style + ".fbx",
                dir + "Attack01_" + suffix + ".fbx",
                dir + "GetHit01_" + style + ".fbx",
                dir + "Die01_" + style + ".fbx",
                dir + "Attack01_" + suffix + ".fbx"
            }, report);
            Finish(profile, go, sourceAvatar, report);
        }

        private static void Select(GameObject go, string prefix, string selected)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix, StringComparison.Ordinal) && (prefix != "Body" || t.name.Length > 4 && char.IsDigit(t.name[4])))
                    t.gameObject.SetActive(t.name == selected);
        }

        private static void BuildCreature(string role, string model, string texture, float height, string[] clips, System.Text.StringBuilder report)
        {
            string modelPath = Source + "/" + model;
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            go.name = role;
            var sourceAvatar = go.GetComponentInChildren<Animator>(true)?.avatar
                ?? AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();

            var material = ToonMaterial(role, AssetDatabase.LoadAssetAtPath<Texture2D>(Source + "/" + texture), Color.white);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();

            var profile = PrepareProfile(role, go, modelPath, height);
            AssignClips(profile, go, clips, report);
            Finish(profile, go, sourceAvatar, report);
        }

        private static Avatar FindAnyAvatarInFolder(string folder)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Avatar", new[] { folder }))
            {
                var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(AssetDatabase.GUIDToAssetPath(guid));
                if (avatar != null) return avatar;
            }
            return null;
        }

        private static UnitAppearance PrepareProfile(string role, GameObject go, string source, float height)
        {
            foreach (var component in go.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in go.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var t in go.GetComponentsInChildren<Transform>(true).Reverse().ToArray())
                if (t != null && t != go.transform && !t.gameObject.activeSelf)
                    UnityEngine.Object.DestroyImmediate(t.gameObject);

            string path = "Assets/Package_Unit/Appearance/Base/" + role + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<UnitAppearance>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<UnitAppearance>();
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.name = role;
            profile.Source = source;
            profile.Height = height;
            return profile;
        }

        private static void AssignClips(UnitAppearance profile, GameObject go, string[] files, System.Text.StringBuilder report, string[] clipNames = null)
        {
            var clips = new AnimationClip[files.Length];
            string[] labels = { "Idle", "Walk", "Attack", "Hit", "Death", "Slam" };
            bool[] loops = { true, true, false, false, false, false };

            for (int i = 0; i < files.Length; i++)
            {
                var sourceClip = AssetDatabase.LoadAllAssetsAtPath(Source + "/" + files[i])
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview", StringComparison.Ordinal) &&
                        (clipNames == null || c.name == clipNames[i]));
                if (sourceClip == null) throw new InvalidOperationException("缺动作 " + files[i] + (clipNames == null ? "" : "/" + clipNames[i]));

                var clip = UnityEngine.Object.Instantiate(sourceClip);
                clip.legacy = false;
                clip.name = profile.name + "_" + labels[i];
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = loops[i];
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());

                var missing = AnimationUtility.GetCurveBindings(clip)
                    .Where(b => !string.IsNullOrEmpty(b.path) && go.transform.Find(b.path) == null)
                    .ToArray();
                report.AppendLine($"{profile.name} {labels[i]} legacy={clip.legacy} len={clip.length:F3} curves={AnimationUtility.GetCurveBindings(clip).Length} missing={missing.Length}");
                if (missing.Length > 0)
                    throw new InvalidOperationException(profile.name + "动作骨架路径不匹配：" + missing[0].path);

                string clipPath = Generated + "/Clips/" + clip.name + ".anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (existing == null) AssetDatabase.CreateAsset(clip, clipPath);
                else
                {
                    EditorUtility.CopySerialized(clip, existing);
                    UnityEngine.Object.DestroyImmediate(clip);
                    clip = existing;
                }
                clips[i] = clip;
            }

            profile.Idle = clips[0];
            profile.Move = clips[1];
            profile.Attack = clips[2];
            profile.Hit = clips[3];
            profile.Death = clips[4];
            profile.Slam = clips[5];
        }

        private static AnimatorController BuildAnimatorController(UnitAppearance profile)
        {
            string controllerPath = Generated + "/Controllers/" + profile.name + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                AssetDatabase.DeleteAsset(controllerPath);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;

            var idleState = sm.AddState("Idle");
            idleState.motion = profile.Idle;
            sm.defaultState = idleState;

            var walkState = sm.AddState("Walk");
            walkState.motion = profile.Move;

            var attackState = sm.AddState("Attack");
            attackState.motion = profile.Attack;
            attackState.speed = Mathf.Max(0.1f, profile.AttackSpeed);

            var hitState = sm.AddState("Hit");
            hitState.motion = profile.Hit;

            var deathState = sm.AddState("Death");
            deathState.motion = profile.Death;

            var slamState = sm.AddState("Slam");
            slamState.motion = profile.Slam;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void Finish(UnitAppearance profile, GameObject go, Avatar avatar, System.Text.StringBuilder report)
        {
            var controller = BuildAnimatorController(profile);
            profile.AnimatorController = controller;

            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // 计算本地高度边界，用于创建标准 UnitMono 挂点（对齐 Demo UnitNodeType）。
            var renderers = go.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float localH = Mathf.Max(0.2f, bounds.size.y);
            float minY = bounds.min.y;

            var unitMono = go.AddComponent<UnitMono>();
            unitMono.Animator = animator;
            unitMono.ActiveRoot = go.transform;
            unitMono.UseRootMotion = false;
            unitMono.UnitNodeDatas = new List<UnitNodeData>
            {
                CreateNode(go.transform, UnitNodeType.Root, "Node_Root", new Vector3(0f, minY, 0f)),
                CreateNode(go.transform, UnitNodeType.BodyCenter, "Node_BodyCenter", new Vector3(0f, minY + localH * 0.52f, 0f)),
                CreateNode(go.transform, UnitNodeType.Spine, "Node_Spine", new Vector3(0f, minY + localH * 0.56f, 0f)),
                CreateNode(go.transform, UnitNodeType.Head, "Node_Head", new Vector3(0f, minY + localH * 0.88f, 0f)),
                CreateNode(go.transform, UnitNodeType.LeftHand, "Node_LeftHand", new Vector3(-localH * 0.25f, minY + localH * 0.55f, localH * 0.18f)),
                CreateNode(go.transform, UnitNodeType.RightHand, "Node_RightHand", new Vector3(localH * 0.25f, minY + localH * 0.55f, localH * 0.22f)),
                CreateNode(go.transform, UnitNodeType.Custom1, "Node_CastPoint", new Vector3(0f, minY + localH * 0.62f, localH * 0.35f)),
            };

            if (profile.Idle != null)
            {
                profile.Idle.SampleAnimation(go, 0f);
            }

            int triangles = renderers.Sum(r => TriangleCount(r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh));
            report.AppendLine($"{profile.name} animator=true controller={controller.name} avatar={(avatar != null ? avatar.name : "none")} nodes={unitMono.UnitNodeDatas.Count} renderers={renderers.Length} triangles={triangles}");

            // 技能编辑器与实战使用同一份预制体，禁止生成 Resources 镜像。
            string pkgDir = PackageUnitRoot + "/" + profile.name;
            Directory.CreateDirectory(pkgDir);
            profile.Model = PrefabUtility.SaveAsPrefabAsset(go, pkgDir + "/" + profile.name + ".prefab");

            EditorUtility.SetDirty(profile);
            UnityEngine.Object.DestroyImmediate(go);
        }

        private static UnitNodeData CreateNode(Transform parent, UnitNodeType nodeType, string name, Vector3 localPos)
        {
            var existing = parent.Find(name);
            var t = existing != null ? existing : new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            return new UnitNodeData { UnitNodeType = nodeType, UnitNodeTrans = t };
        }

        private static int TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            int count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) count += (int)mesh.GetIndexCount(i) / 3;
            return count;
        }

        private static Material ToonMaterial(string name, Texture texture, Color color)
        {
            string path = Generated + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Roco/Character")); AssetDatabase.CreateAsset(material, path); }
            material.shader = Shader.Find("Roco/Character");
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_DyeStrength", 0);
            material.SetFloat("_AOStrength", 0);
            material.SetFloat("_ToonLit", 1);
            material.SetFloat("_ToonMid", .64f);
            material.SetFloat("_ToonShadow", .32f);
            material.SetFloat("_AmbientStrength", .55f);
            material.SetFloat("_OutlineUseSmoothNormal", 0);
            material.SetFloat("_OutlineWidthMode", 1);
            material.SetFloat("_OutlineWidth", .8f);
            material.SetFloat("_ReceiveShadows", 0);
            material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            EditorUtility.SetDirty(material);
            return material;
        }
    }

    internal sealed class UnitSourceImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(UnitAssetBuilder.Source + "/", StringComparison.Ordinal)) return;
            var model = (ModelImporter)assetImporter;
            model.animationType = ModelImporterAnimationType.Generic;
            model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            model.importAnimation = UnitAssetBuilder.HasUnitAnimations(assetPath);
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
