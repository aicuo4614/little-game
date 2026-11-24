using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor
{
    /// <summary>
    /// 将提取自 PELibDemo Hovl Studio 的特效源 Prefab 清洗为本项目 URP 14 精简特效预制体：
    /// 剥离碰撞体/刚体/音频/缺失脚本，检查并适配 URP 粒子 Shader，按 EffectId 输出到 Resources/SkillEffects。
    /// </summary>
    public static class SkillEffectAssetBuilder
    {
        private const string SourceRoot = "Assets/LittleGame/Art/SkillEffectsSources/HovlStudio";
        private const string OutputRoot = "Assets/LittleGame/Resources/SkillEffects";
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
                        bool supported = m.shader != null && m.shader.isSupported && !m.shader.name.Contains("Error");
                        if (!supported && fallbackShader != null)
                        {
                            if (!convertedMats.TryGetValue(m, out var urpMat))
                            {
                                urpMat = new Material(fallbackShader);
                                if (m.HasProperty("_MainTex")) urpMat.SetTexture("_BaseMap", m.GetTexture("_MainTex"));
                                Color col = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                                // 精确 HDR 增益（~1.35x）越过 Selective Bloom 阈值 1.08，同时用柔和 AlphaBlend 防止叠成实心白块
                                float peak = Mathf.Max(0.01f, Mathf.Max(col.r, Mathf.Max(col.g, col.b)));
                                float hdrBoost = peak > 0.15f ? 1.32f / peak : 1.15f;
                                col = new Color(col.r * hdrBoost, col.g * hdrBoost, col.b * hdrBoost, Mathf.Clamp(col.a * 0.58f, 0.24f, 0.58f));
                                urpMat.SetColor("_BaseColor", col);
                                urpMat.SetFloat("_Surface", 1f); // Transparent
                                urpMat.SetFloat("_Blend", 0f);   // Alpha Blend（避免多层叠加过曝成白块）
                                urpMat.SetFloat("_ColorMode", 0f);
                                urpMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                                urpMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                                urpMat.SetFloat("_ZWrite", 0f);
                                urpMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                                urpMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                                urpMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                urpMat.DisableKeyword("_ALPHATEST_ON");
                                urpMat.SetOverrideTag("RenderType", "Transparent");
                                urpMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                                string matAssetPath = $"{GeneratedMatRoot}/{Sanitize(m.name)}_URP.mat";
                                if (File.Exists(matAssetPath)) AssetDatabase.DeleteAsset(matAssetPath);
                                AssetDatabase.CreateAsset(urpMat, matAssetPath);
                                convertedMats[m] = urpMat;
                                convertedCount++;
                            }
                            mats[i] = urpMat;
                            changed = true;
                            unsupportedFixed++;
                        }
                    }
                    if (changed) r.sharedMaterials = mats;
                }

                string outPath = $"{OutputRoot}/{spec.EffectId}.prefab";
                if (File.Exists(outPath)) AssetDatabase.DeleteAsset(outPath);
                PrefabUtility.SaveAsPrefabAsset(root, outPath);
                UnityEngine.Object.DestroyImmediate(root);
                built++;
                lines.Add($"OK {spec.EffectId} scale={spec.Scale} urpConverted={unsupportedFixed} <- {spec.SourceRelPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            lines.Insert(0, $"SkillEffectAssetBuilder: built={built}, missing={missing}, convertedMaterials={convertedCount}");
            File.WriteAllLines(ReportPath, lines);
            Debug.Log($"[SkillEffects] 构建完成: built={built}, missing={missing}, convertedMaterials={convertedCount}");
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "EffectMat" : name;
        }
    }
}
