using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 战斗特效、对象池 VFX 与飘字子系统（对齐 Demo BattleEffectControl）：
    /// 供共享 TimelinePlayback 的 SpawnEffect/DespawnEffect 回调、伤害飘字与升星烟雾复用。
    /// </summary>
    public sealed class BattleEffectControl : BattleControlBase
    {
        private readonly Dictionary<string, GameObject> effectPrefabCache =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<GameObject, GameObject> instanceToPrefab =
            new Dictionary<GameObject, GameObject>();

        private BattleFloatingText floatingText;
        private Transform fxRoot;

        private static Texture2D sharedSmokeTex;
        private static Material sharedSmokeMat;

        public BattleFloatingText FloatingText => floatingText;

        public override void Init(BattleView owner)
        {
            base.Init(owner);
            fxRoot = new GameObject("BattleEffectRoot").transform;
            fxRoot.SetParent(owner.WorldRoot, false);

            floatingText = new GameObject("BattleFloatingText").AddComponent<BattleFloatingText>();
            floatingText.transform.SetParent(owner.WorldRoot, false);
            floatingText.Initialize(owner.CameraControl.Camera);
        }

        public GameObject SpawnEffect(string logicalName)
        {
            var prefab = LoadEffectPrefab(logicalName);
            if (prefab == null)
            {
                // 缺失时返回空占位节点，避免中断整段技能演出
                var fallback = new GameObject("MissingEffect_" + logicalName);
                fallback.transform.SetParent(fxRoot, false);
                return fallback;
            }

            GameObject instance;
            if (Owner.PrefabPool != null)
            {
                instance = Owner.PrefabPool.Spawn("Effect/Low/" + logicalName, fxRoot, ResourcePackageName.EffectPackage);
                instanceToPrefab[instance] = prefab;
            }
            else
            {
                instance = UnityEngine.Object.Instantiate(prefab, fxRoot);
            }

            instance.SetActive(true);
            return instance;
        }

        public void DespawnEffect(GameObject instance)
        {
            if (instance == null) return;
            if (instanceToPrefab.TryGetValue(instance, out var prefab))
            {
                instanceToPrefab.Remove(instance);
                if (Owner.PrefabPool != null && prefab != null)
                {
                    instance.SetActive(false);
                    Owner.PrefabPool.Despawn(instance);
                    return;
                }
            }
            UnityEngine.Object.Destroy(instance);
        }

        public void SpawnFloatingText(Vector3 worldPos, string text, Color color, float scale = 1f, float duration = 0.6f)
        {
            floatingText?.Spawn(worldPos, text, color, scale, duration);
        }

        public void Step(float scaledDt, bool paused)
        {
            floatingText?.Step(scaledDt, paused);
        }

        public void ClearTransient()
        {
            floatingText?.ClearAll();
        }

        public void SpawnMergeSmokeVfx(Vector3 worldPos, int star)
        {
            EnsureSmokeMaterial();

            var root = new GameObject("MergeSmokeVfx");
            root.transform.SetParent(fxRoot, false);
            root.transform.position = worldPos;

            var smokePs = root.AddComponent<ParticleSystem>();
            smokePs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smokePs.main;
            main.playOnAwake = false;
            main.duration = 0.65f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.80f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.95f, 2.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.55f, 1.05f);
            Color tint = star >= 3 ? new Color(1f, 0.84f, 0.30f, 0.78f) : new Color(0.42f, 0.86f, 1f, 0.78f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.96f, 0.98f, 1f, 0.86f), tint);
            main.gravityModifier = -0.22f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = smokePs.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

            var shape = smokePs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;

            var sizeOverLifetime = smokePs.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f),
                new Keyframe(0.35f, 1.25f),
                new Keyframe(1f, 1.55f)));

            var colorOverLifetime = smokePs.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.96f, 0.80f), 0f), new GradientColorKey(new Color(0.96f, 0.96f, 0.98f), 1f) },
                new[] { new GradientAlphaKey(0.88f, 0f), new GradientAlphaKey(0.58f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = grad;

            var renderer = smokePs.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = sharedSmokeMat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            smokePs.Play();
            UnityEngine.Object.Destroy(root, 1.6f);
        }

        private GameObject LoadEffectPrefab(string logicalName)
        {
            if (string.IsNullOrEmpty(logicalName)) return null;
            if (effectPrefabCache.TryGetValue(logicalName, out var cached)) return cached;

            GameObject prefab = null;
#if UNITY_EDITOR
            var candidates = new[]
            {
                "Assets/Package_Effect/Low/Base/" + logicalName + ".prefab",
                "Assets/Package_Effect/Low/OnDemand/" + logicalName + ".prefab",
                "Assets/LittleGame/Resources/SkillEffects/" + logicalName + ".prefab",
            };
            for (int i = 0; i < candidates.Length && prefab == null; i++)
            {
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(candidates[i]);
            }
#endif
            if (prefab == null)
            {
                prefab = Resources.Load<GameObject>("SkillEffects/" + logicalName);
            }

            effectPrefabCache[logicalName] = prefab;
            return prefab;
        }

        private static void EnsureSmokeMaterial()
        {
            if (sharedSmokeMat != null) return;
            const int size = 64;
            sharedSmokeTex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half)) / half;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), 1.8f);
                    sharedSmokeTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            sharedSmokeTex.Apply();

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            sharedSmokeMat = new Material(shader);
            sharedSmokeMat.mainTexture = sharedSmokeTex;
            if (sharedSmokeMat.HasProperty("_BaseMap")) sharedSmokeMat.SetTexture("_BaseMap", sharedSmokeTex);
            sharedSmokeMat.SetFloat("_Surface", 1f);
            sharedSmokeMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            sharedSmokeMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            sharedSmokeMat.SetFloat("_ZWrite", 0f);
            sharedSmokeMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            sharedSmokeMat.SetOverrideTag("RenderType", "Transparent");
            sharedSmokeMat.renderQueue = (int)RenderQueue.Transparent + 20;
        }

        public override void ReleaseResources()
        {
            ClearTransient();
            instanceToPrefab.Clear();
            effectPrefabCache.Clear();
            if (floatingText != null) UnityEngine.Object.Destroy(floatingText.gameObject);
            if (fxRoot != null) UnityEngine.Object.Destroy(fxRoot.gameObject);
            floatingText = null;
            fxRoot = null;
        }
    }
}
