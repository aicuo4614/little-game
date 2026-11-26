using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client.Editor
{
    /// <summary>
    /// 将提取自 PELibDemo Hovl Studio 的特效源 Prefab 清洗为本项目 URP 14 精简特效预制体：
    /// 剥离碰撞体/刚体/音频/缺失脚本，检查并适配 URP 粒子 Shader，按 EffectId 输出到 Package_Effect。
    /// </summary>
    public static class SkillEffectAssetBuilder
    {
        private const string SourceRoot = "Assets/LittleGame/Art/SkillEffectsSources/HovlStudio";
        private const string OutputRoot = "Assets/Package_Effect/Low/Base";
        private const string GeneratedMatRoot = "Assets/LittleGame/Art/SkillEffectsGenerated/Materials";
        private const string ReportPath = "Reports/Art/skill-effects-build.txt";

        private sealed class EffectSpec
        {
            public string EffectId;
            public string SourceRelPath;
            public float Scale;
            public EffectSpec(string id, string path, float scale) { EffectId = id; SourceRelPath = path; Scale = scale; }
        }

        private static readonly EffectSpec[] Specs =
        {
            // 盾卫
            new EffectSpec("fx_shield_slash", "AOE Magic spells Vol.1/Prefabs/Flower slash.prefab", 0.55f),
            new EffectSpec("fx_shield_bash", "AOE Magic spells Vol.1/Prefabs/Flower slash.prefab", 0.72f),
            new EffectSpec("fx_shield_guard", "Magic circles/Prefabs/Loop version/Magic shield 1 loop.prefab", 0.42f),
            new EffectSpec("fx_shield_bastion", "Magic circles/Prefabs/Loop version/Magic shield 1 loop.prefab", 0.55f),
            new EffectSpec("fx_shield_dome", "RPG VFX Bundle/Prefabs/Magic buffs and hits/Soft blue buff.prefab", 0.55f),
            new EffectSpec("fx_warcry_wave", "RPG VFX Bundle/Prefabs/Magic buffs and hits/Buff 1.prefab", 0.78f),
            // 风弓手
            new EffectSpec("fx_arrow_basic", "AAA Projectiles Vol 1/Prefabs/Projectiles(transform)/Projectile 1.prefab", 0.48f),
            new EffectSpec("fx_arrow_wind", "AAA Projectiles Vol 1/Prefabs/Projectiles(transform)/Projectile 1.prefab", 0.65f),
            new EffectSpec("fx_arrow_pierce", "AAA Projectiles Vol 1/Prefabs/Projectiles(transform)/Projectile 4.prefab", 0.68f),
            new EffectSpec("fx_arrow_gale", "AAA Projectiles Vol 1/Prefabs/Projectiles(transform)/Projectile 1.prefab", 0.72f),
            new EffectSpec("fx_wind_burst", "AOE Magic spells Vol.1/Prefabs/Leaves buff.prefab", 0.60f),
            new EffectSpec("fx_pierce_flash", "AAA Projectiles Vol 1/Prefabs/Hit 4.prefab", 0.65f),
            new EffectSpec("fx_gale_ring", "AOE Magic spells Vol.1/Prefabs/Leaves buff.prefab", 0.78f),
            // 雷法师
            new EffectSpec("fx_lightning_orb", "AAA Projectiles Vol 1/Prefabs/Projectiles(transform)/Projectile 4.prefab", 0.52f),
            new EffectSpec("fx_chain_lightning", "AAA Projectiles Vol 1/Prefabs/Projectiles(transform)/Projectile 7.prefab", 0.62f),
            new EffectSpec("fx_lightning_strike", "AOE Magic spells Vol.1/Prefabs/Lightning strike.prefab", 0.58f),
            new EffectSpec("fx_storm_zone", "Magic circles/Prefabs/Loop version/Magic circle 1 loop.prefab", 0.62f),
            new EffectSpec("fx_storm_tick", "AOE Magic spells Vol.1/Prefabs/Lightning hit.prefab", 0.42f),
            // 冰法师
            new EffectSpec("fx_ice_shard", "AAA Projectiles Vol 1/Prefabs/Projectiles(transform)/Projectile 7.prefab", 0.48f),
            new EffectSpec("fx_ice_lance", "AAA Projectiles Vol 1/Prefabs/Projectiles(transform)/Projectile 7.prefab", 0.64f),
            new EffectSpec("fx_freeze_ring", "AOE Magic spells Vol.1/Prefabs/Ise attack.prefab", 0.52f),
            new EffectSpec("fx_freeze_aura", "RPG VFX Bundle/Prefabs/Magic buffs and hits/Soft blue buff.prefab", 0.52f),
            new EffectSpec("fx_ice_armor", "RPG VFX Bundle/Prefabs/Magic buffs and hits/Soft blue buff.prefab", 0.75f),
            // 通用受击
            new EffectSpec("fx_hit_physical", "RPG VFX Bundle/Prefabs/Magic buffs and hits/Punch Hit.prefab", 0.45f),
            new EffectSpec("fx_hit_heavy", "RPG VFX Bundle/Prefabs/Magic buffs and hits/Punch Hit.prefab", 0.62f),
            new EffectSpec("fx_hit_arrow", "AAA Projectiles Vol 1/Prefabs/Hit 1.prefab", 0.48f),
            new EffectSpec("fx_hit_wind", "AAA Projectiles Vol 1/Prefabs/Hit 1.prefab", 0.62f),
            new EffectSpec("fx_hit_pierce", "AAA Projectiles Vol 1/Prefabs/Hit 4.prefab", 0.65f),
            new EffectSpec("fx_hit_lightning", "AOE Magic spells Vol.1/Prefabs/Lightning hit.prefab", 0.55f),
            new EffectSpec("fx_hit_ice", "AAA Projectiles Vol 1/Prefabs/Hit 7.prefab", 0.56f),
            new EffectSpec("fx_enemy_hit", "AAA Projectiles Vol 1/Prefabs/Hit 4.prefab", 0.42f),
            new EffectSpec("fx_hit_enemy", "AAA Projectiles Vol 1/Prefabs/Hit 4.prefab", 0.45f),
            // 治疗与首领
            new EffectSpec("fx_heal_orb", "AAA Projectiles Vol 1/Prefabs/Projectiles(transform)/Projectile 1.prefab", 0.48f),
            new EffectSpec("fx_heal_ring", "RPG VFX Bundle/Prefabs/Magic buffs and hits/Heal.prefab", 0.58f),
            new EffectSpec("fx_boss_charge", "Magic circles/Prefabs/Loop version/Magic circle 1 loop.prefab", 0.92f),
            new EffectSpec("fx_boss_slam_wave", "AOE Magic spells Vol.1/Prefabs/Energy explosion.prefab", 0.85f),
            new EffectSpec("fx_boss_slam_burst", "AOE Magic spells Vol.1/Prefabs/Energy explosion.prefab", 0.62f),
            // 战术与升星
            new EffectSpec("fx_tactic_protect", "Magic circles/Prefabs/Loop version/Magic shield 1 loop.prefab", 0.72f),
            new EffectSpec("fx_tactic_focus", "Magic circles/Prefabs/Loop version/Magic circle 1 loop.prefab", 0.50f),
            new EffectSpec("fx_level_up", "RPG VFX Bundle/Prefabs/Magic buffs and hits/Buff 1.prefab", 0.68f),
        };

        [MenuItem("Little Game/Combat/Build Skill Effects")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(OutputRoot);
            Directory.CreateDirectory(GeneratedMatRoot);

            var fallbackShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var convertedMats = new Dictionary<Material, Material>();
            var lines = new List<string>();
            int built = 0;
            int missing = 0;
            int convertedCount = 0;

            foreach (var spec in Specs)
            {
                string srcPath = SourceRoot + "/" + spec.SourceRelPath;
                var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
                if (srcPrefab == null)
                {
                    missing++;
                    lines.Add($"MISSING {spec.EffectId} <- {srcPath}");
                    continue;
                }
                int sourceParticleCount = srcPrefab.GetComponentsInChildren<ParticleSystem>(true).Length;
                if (sourceParticleCount == 0)
                    throw new InvalidDataException("特效源缺少粒子组件，停止生成：" + srcPath);

                var root = new GameObject(spec.EffectId);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(srcPrefab);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.transform.SetParent(root.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one * spec.Scale;

                // 清理碰撞体、刚体、光源（先移除 URP 附加灯光组件再删 Light）、音频与源工程演示脚本（缺失脚本）
                foreach (var col in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
                foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(rb);
                foreach (var audio in root.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(audio);
                foreach (var uld in root.GetComponentsInChildren<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>(true)) UnityEngine.Object.DestroyImmediate(uld);
                foreach (var light in root.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

                // 确保粒子使用 ScalingMode.Hierarchy 并且自动播放
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.playOnAwake = true;
                }

                // 核查材质 Shader 兼容性，不支持时转为 URP Particles/Unlit
                int unsupportedFixed = 0;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null) continue;
                        bool supported = m.shader != null && m.shader.isSupported
                            && m.shader.name.StartsWith("Universal Render Pipeline/");
                        if (!supported && fallbackShader != null)
                        {
                            if (!convertedMats.TryGetValue(m, out var urpMat))
                            {
                                urpMat = new Material(fallbackShader);
                                // 丢失旧 Shader 时 HasProperty 为 false，但纹理仍保存在序列化属性中。
                                // 必须读取原始保存值，否则所有粒子会变成实心方片。
                                var saved = new SerializedObject(m);
                                var texEnvs = saved.FindProperty("m_SavedProperties.m_TexEnvs");
                                for (int t = 0; t < texEnvs.arraySize; t++)
                                {
                                    var entry = texEnvs.GetArrayElementAtIndex(t);
                                    if (entry.FindPropertyRelative("first").stringValue != "_MainTex") continue;
                                    var value = entry.FindPropertyRelative("second");
                                    urpMat.SetTexture("_BaseMap", value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture);
                                    urpMat.SetTextureScale("_BaseMap", value.FindPropertyRelative("m_Scale").vector2Value);
                                    urpMat.SetTextureOffset("_BaseMap", value.FindPropertyRelative("m_Offset").vector2Value);
                                }
                                Color col = Color.white;
                                var colors = saved.FindProperty("m_SavedProperties.m_Colors");
                                for (int c = 0; c < colors.arraySize; c++)
                                {
                                    var entry = colors.GetArrayElementAtIndex(c);
                                    if (entry.FindPropertyRelative("first").stringValue == "_TintColor")
                                        col = entry.FindPropertyRelative("second").colorValue;
                                }
                                // 精确 HDR 增益（~1.35x）越过 Selective Bloom 阈值 1.08，同时用柔和 AlphaBlend 防止叠成实心白块
                                float peak = Mathf.Max(0.01f, Mathf.Max(col.r, Mathf.Max(col.g, col.b)));
                                float hdrBoost = peak > 0.15f ? 1.32f / peak : 1.15f;
                                col = new Color(col.r * hdrBoost, col.g * hdrBoost, col.b * hdrBoost, Mathf.Clamp(col.a * 0.58f, 0.24f, 0.58f));
                                urpMat.SetColor("_BaseColor", col);
                                urpMat.SetFloat("_Surface", 1f); // Transparent
                                urpMat.SetFloat("_Blend", 2f);   // 原素材用黑底发光贴图，Additive 避免不透明方片
                                urpMat.SetFloat("_ColorMode", 0f);
                                urpMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                                urpMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                                urpMat.SetFloat("_ZWrite", 0f);
                                urpMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                                urpMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                                urpMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                urpMat.DisableKeyword("_ALPHATEST_ON");
                                urpMat.SetOverrideTag("RenderType", "Transparent");
                                urpMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                                string matAssetPath = $"{GeneratedMatRoot}/{Sanitize(m.name)}_URP.mat";
                                var existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(matAssetPath);
                                urpMat.name = m.name + "_URP";
                                if (existingMaterial)
                                {
                                    EditorUtility.CopySerialized(urpMat, existingMaterial);
                                    UnityEngine.Object.DestroyImmediate(urpMat);
                                    urpMat = existingMaterial;
                                    EditorUtility.SetDirty(urpMat);
                                }
                                else AssetDatabase.CreateAsset(urpMat, matAssetPath);
                                convertedMats[m] = urpMat;
                                convertedCount++;
                            }
                            mats[i] = urpMat;
                            changed = true;
                            unsupportedFixed++;
                        }
                    }
                    if (changed) r.sharedMaterials = mats;
                    if (r is ParticleSystemRenderer particleRenderer)
                    {
                        // 第二材质槽是可选拖尾；使用 URP 材质，防止启用拖尾时出现粉色大片。
                        var ps = r.GetComponent<ParticleSystem>();
                        if (ps && ps.trails.enabled && !particleRenderer.trailMaterial)
                            particleRenderer.trailMaterial = particleRenderer.sharedMaterial;
                    }
                }

                string outPath = $"{OutputRoot}/{spec.EffectId}.prefab";
                if (spec.EffectId == "fx_shield_slash" || spec.EffectId == "fx_shield_bash")
                {
                    instance.SetActive(false);
                    AddBladeArc(root, fallbackShader, spec.EffectId == "fx_shield_bash" ? 0.8f : 0.62f);
                }
                SkillEffectAccentBuilder.Add(root, instance, spec.EffectId, fallbackShader);
                int expectedParticleCount = root.GetComponentsInChildren<ParticleSystem>(true).Length;
                PrefabUtility.SaveAsPrefabAsset(root, outPath);
                int savedParticleCount = AssetDatabase.LoadAssetAtPath<GameObject>(outPath).GetComponentsInChildren<ParticleSystem>(true).Length;
                if (savedParticleCount != expectedParticleCount)
                    throw new InvalidDataException($"特效组件丢失：{spec.EffectId}, source={sourceParticleCount}, saved={savedParticleCount}");
                UnityEngine.Object.DestroyImmediate(root);
                built++;
                lines.Add($"OK {spec.EffectId} particles={savedParticleCount} scale={spec.Scale} urpConverted={unsupportedFixed} <- {spec.SourceRelPath}");
            }

            AssetDatabase.SaveAssets();
            // 先落盘材质，再强制重载 Prefab 的引用，避免旧实例缓存仍持有已替换材质。
            AssetDatabase.ImportAsset(GeneratedMatRoot, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
            AssetDatabase.ImportAsset(OutputRoot, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
            AssetDatabase.Refresh();

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            lines.Insert(0, $"SkillEffectAssetBuilder: built={built}, missing={missing}, convertedMaterials={convertedCount}");
            File.WriteAllLines(ReportPath, lines);
            Debug.Log($"[SkillEffects] 构建完成: built={built}, missing={missing}, convertedMaterials={convertedCount}");
        }

        // 普攻使用清晰、短促的几何刀光；不让花叶法术掩盖武器接触。
        private static void AddBladeArc(GameObject root, Shader shader, float radius)
        {
            const string meshPath = "Assets/LittleGame/Art/SkillEffectsGenerated/BladeArc.asset";
            const string matPath = "Assets/LittleGame/Art/SkillEffectsGenerated/Materials/BladeArc.mat";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh)
            {
                mesh = new Mesh { name = "BladeArc" };
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                const int segments = 24;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = Mathf.Lerp(-75f, 75f, i / (float)segments) * Mathf.Deg2Rad;
                    float thickness = Mathf.Sin(i / (float)segments * Mathf.PI) * 0.18f;
                    var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    vertices.Add(dir); vertices.Add(dir * (1f - thickness));
                    if (i == segments) continue;
                    int v = i * 2;
                    triangles.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                }
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!material)
            {
                material = new Material(shader) { name = "BladeArc", renderQueue = 3000 };
                material.SetFloat("_Surface", 1f); material.SetFloat("_Blend", 2f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.One);
                material.SetFloat("_ZWrite", 0f); material.SetFloat("_Cull", (float)CullMode.Off);
                material.SetColor("_BaseColor", Color.white);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                AssetDatabase.CreateAsset(material, matPath);
            }
            var go = new GameObject("BladeArc"); go.transform.SetParent(root.transform, false);
            var particle = go.AddComponent<ParticleSystem>();
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particle.main;
            main.duration = 0.14f; main.loop = false; main.playOnAwake = true; main.startLifetime = 0.14f;
            main.startSpeed = 0f; main.startSize = radius; main.maxParticles = 1;
            main.startColor = new Color(1.4f, 1.1f, 0.55f, 0.9f); main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var shape = particle.shape; shape.enabled = false;
            var emission = particle.emission; emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var fade = particle.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.mesh = mesh; renderer.sharedMaterial = material;
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "EffectMat" : name;
        }
    }
}
