using LittleGame.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LittleGame.Client.Battle
{
    /// <summary>战场环境、光照与选择性后处理子系统（对齐 Demo BattleSceneControl）。</summary>
    public sealed class BattleSceneControl : BattleControlBase
    {
        private CourtyardEnvironment environment;
        private Light combatSun;
        private Volume bloomVolume;

        public Light CombatSun => combatSun;
        public CourtyardEnvironment Environment => environment;

        public override void Init(BattleView owner)
        {
            base.Init(owner);

            bool isMobile = Application.isMobilePlatform;
            if (!isMobile)
            {
                bloomVolume = SetupSelectiveEffectBloom(owner.WorldRoot);
            }

            foreach (var old in Object.FindObjectsOfType<Light>()) old.enabled = false;
            combatSun = new GameObject("CombatSun").AddComponent<Light>();
            combatSun.transform.SetParent(owner.WorldRoot);
            combatSun.type = LightType.Directional;
            combatSun.transform.rotation = Quaternion.Euler(46f, -28f, 0f);
            combatSun.shadows = isMobile ? LightShadows.Hard : LightShadows.Soft;
            combatSun.color = new Color(1f, 0.96f, 0.88f);
            combatSun.intensity = 1.18f;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.65f, 0.71f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.57f, 0.59f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.35f, 0.40f);

            environment = new GameObject("InfiniteRuinsRoad").AddComponent<CourtyardEnvironment>();
            environment.transform.SetParent(owner.WorldRoot, false);
            environment.Build();
        }

        public void SyncEnvironment(BattleSession session, bool paused, float deltaCamX, float camCurrentX, float dt, out bool marching)
        {
            float avgMoveSpeed = BattleDefinitions.FixedTroopMoveSpeed;
            float scrollSpeed = paused || session.Phase == RunPhase.Lost || session.Phase == RunPhase.Won
                ? 0f
                : session.Phase == RunPhase.Preparation
                    ? (session.IsRegrouping ? avgMoveSpeed * 0.45f : avgMoveSpeed * 0.72f)
                    : (session.Phase == RunPhase.WaveResult || session.Phase == RunPhase.Reward)
                        ? (session.IsRegrouping ? avgMoveSpeed * 0.65f : avgMoveSpeed * 1.45f)
                        : 0f;

            environment?.Apply(scrollSpeed, deltaCamX, camCurrentX, dt);
            marching = scrollSpeed > 0.1f;
        }

        public object Snapshot() => environment != null ? environment.Snapshot() : null;

        public override void ReleaseResources()
        {
            if (bloomVolume != null)
            {
                if (bloomVolume.sharedProfile != null) Object.Destroy(bloomVolume.sharedProfile);
                Object.Destroy(bloomVolume.gameObject);
                bloomVolume = null;
            }
            if (environment != null)
            {
                Object.Destroy(environment.gameObject);
                environment = null;
            }
            if (combatSun != null)
            {
                Object.Destroy(combatSun.gameObject);
                combatSun = null;
            }
        }

        private static Volume SetupSelectiveEffectBloom(Transform parent)
        {
            var go = new GameObject("SelectiveEffectBloomVolume");
            go.transform.SetParent(parent, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.08f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.62f);
            bloom.clamp.Override(4.0f);
            bloom.highQualityFiltering.Override(false);
            vol.sharedProfile = profile;
            return vol;
        }
    }
}
