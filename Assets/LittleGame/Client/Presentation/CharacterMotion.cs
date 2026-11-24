using System;
using System.Linq;
using UnityEngine;

namespace LittleGame.Client
{
    /// <summary>Legacy 骨架表现与打击反馈适配（受击闪白、弹性脉冲、局部顿帧）。动作不回写规则。</summary>
    public sealed class CharacterMotion : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Animation animationPlayer;
        private Renderer[] cachedRenderers;
        private MaterialPropertyBlock flashBlock;
        private Vector3 previous;
        private bool hasAttack;
        private bool dying;
        private float deathTime;
        private float attackSpeed = 1.5f;

        private float hitPulseRemaining;
        private float hitPulseDuration = 0.12f;
        private Color hitFlashColor = Color.white;
        private float hitStopRemaining;
        private bool flashActive;
        private Vector3 baseLocalScale = Vector3.one;

        public string CurrentAction { get; private set; }
        public bool DeathFinished => dying && deathTime >= (animationPlayer["Death"]?.length ?? 0);
        public float ActionTime => animationPlayer != null && CurrentAction != null && animationPlayer[CurrentAction] != null ? animationPlayer[CurrentAction].time : 0;

        public void Configure(UnitAppearance appearance)
        {
            animationPlayer = GetComponent<Animation>() ?? gameObject.AddComponent<Animation>();
            animationPlayer.enabled = true;
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            flashBlock = new MaterialPropertyBlock();
            attackSpeed = appearance.AttackSpeed;
            AddClip("Idle", appearance.Idle, true); AddClip("Walk", appearance.Move, true);
            hasAttack = AddClip("Attack", appearance.Attack, false);
            AddClip("Hit", appearance.Hit, false); AddClip("Death", appearance.Death, false); AddClip("Slam", appearance.Slam, false);
            baseLocalScale = transform.localScale;
            Play("Idle"); animationPlayer.Sample(); previous = transform.position;
        }

        private bool AddClip(string alias, AnimationClip clip, bool loop)
        {
            if (clip == null) return false;
            animationPlayer.AddClip(clip, alias); animationPlayer[alias].wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            return true;
        }

        private void Play(string alias)
        {
            if (animationPlayer == null || animationPlayer[alias] == null) return;
            CurrentAction = alias; animationPlayer.Play(alias); animationPlayer[alias].time = 0;
        }

        public void SetAlive(bool alive)
        {
            if (!alive && !dying)
            {
                dying = true; deathTime = 0; hitStopRemaining = 0; ClearFlash();
                Play("Death");
            }
            else if (alive && dying)
            {
                dying = false; deathTime = 0; hitStopRemaining = 0; ClearFlash();
                Play("Idle");
            }
        }

        // 取消受击骨架动画，避免移动中受击产生滑步；改由高亮闪白 + 弹性脉冲表现打击感。
        public void Hit() { TriggerHitFeedback(0.12f, Color.white); }
        public void Slam() { if (!dying) Play("Slam"); }

        public void PlayAction(string alias, float speed = 1f)
        {
            if (dying || animationPlayer == null || !isActiveAndEnabled || string.IsNullOrWhiteSpace(alias)) return;
            if (alias == "Hit") return;
            if (animationPlayer[alias] == null) return;
            Play(alias);
            float effective = alias == "Attack" ? attackSpeed * Mathf.Max(0.1f, speed) : Mathf.Max(0.1f, speed);
            animationPlayer[alias].speed = effective;
        }

        public void TriggerHitPulse(float duration = 0.12f) => TriggerHitFeedback(duration, Color.white);

        public void TriggerHitFeedback(float duration, Color flashColor)
        {
            if (dying) return;
            hitPulseDuration = Mathf.Max(0.05f, duration);
            hitPulseRemaining = hitPulseDuration;
            hitFlashColor = flashColor;
        }

        public void TriggerHitStop(float duration)
        {
            if (dying || duration <= 0f) return;
            hitStopRemaining = Mathf.Max(hitStopRemaining, Mathf.Min(0.16f, duration));
        }

        public void Configure(int kind)
        {
            animationPlayer = GetComponent<Animation>() ?? gameObject.AddComponent<Animation>(); animationPlayer.enabled = true;
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            flashBlock = new MaterialPropertyBlock();
            Add("Idle", "Rig_Medium_General", "Idle_A", true);
            Add("Walk", "Rig_Medium_MovementBasic", "Walking_A", true);
            hasAttack = Add("Attack", kind == 0 ? "Rig_Medium_CombatMelee" : "Rig_Medium_CombatRanged", kind == 0 ? "Melee_1H_Attack_Chop" : kind == 1 ? "Ranged_Bow_Release" : "Ranged_Magic_Spellcasting", false);
            if (animationPlayer["Idle"] != null) { animationPlayer.Play("Idle"); animationPlayer.Sample(); }
            baseLocalScale = transform.localScale;
            previous = transform.position;
        }

        private bool Add(string alias, string file, string clipName, bool loop)
        {
            var clip = Resources.LoadAll<AnimationClip>("CombatCharacters/" + file).FirstOrDefault(c => c.name == clipName);
            if (clip == null) return false;
            animationPlayer.AddClip(clip, alias); animationPlayer[alias].wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            return true;
        }

        public void Attack()
        {
            if (!hasAttack || !isActiveAndEnabled || dying) return;
            Play("Attack"); animationPlayer["Attack"].speed = attackSpeed;
        }

        public void Apply(bool paused, bool forceWalk = false)
        {
            if (animationPlayer == null || !isActiveAndEnabled) return;
            if (!paused && hitStopRemaining > 0f)
                hitStopRemaining = Mathf.Max(0f, hitStopRemaining - Time.unscaledDeltaTime);

            bool frozenAnim = paused || hitStopRemaining > 0f;
            foreach (AnimationState state in animationPlayer)
                state.speed = frozenAnim ? 0 : state.name == "Attack" ? attackSpeed : 1;

            if (paused) { previous = transform.position; return; }

            if (hitPulseRemaining > 0f)
            {
                hitPulseRemaining = Mathf.Max(0f, hitPulseRemaining - Time.unscaledDeltaTime);
                float k = Mathf.Sin((hitPulseRemaining / Mathf.Max(0.01f, hitPulseDuration)) * Mathf.PI);
                transform.localScale = new Vector3(
                    baseLocalScale.x * (1f + 0.14f * k),
                    baseLocalScale.y * (1f - 0.08f * k),
                    baseLocalScale.z * (1f + 0.14f * k));
                ApplyFlash(k);
            }
            else
            {
                if (baseLocalScale != Vector3.zero) transform.localScale = baseLocalScale;
                if (flashActive) ClearFlash();
            }

            if (dying) { deathTime += Time.unscaledDeltaTime; return; }
            if (hitStopRemaining > 0f) { previous = transform.position; return; }

            var delta = transform.position - previous;
            bool moving = forceWalk || (delta.x * delta.x + delta.z * delta.z > .000001f); previous = transform.position;
            if (animationPlayer.IsPlaying("Attack") || animationPlayer.IsPlaying("Hit") || animationPlayer.IsPlaying("Slam")) return;
            string desired = moving && animationPlayer["Walk"] != null ? "Walk" : "Idle";
            if (animationPlayer[desired] != null && !animationPlayer.IsPlaying(desired)) { CurrentAction = desired; animationPlayer.CrossFade(desired, .1f); }
        }

        private void ApplyFlash(float strength)
        {
            if (cachedRenderers == null || flashBlock == null) return;
            flashActive = true;
            Color boosted = Color.Lerp(Color.white, new Color(
                Mathf.Max(1.6f, hitFlashColor.r * 2.2f),
                Mathf.Max(1.6f, hitFlashColor.g * 2.2f),
                Mathf.Max(1.6f, hitFlashColor.b * 2.2f), 1f), strength);
            flashBlock.SetColor(BaseColorId, boosted);
            for (int i = 0; i < cachedRenderers.Length; i++)
                if (cachedRenderers[i] != null) cachedRenderers[i].SetPropertyBlock(flashBlock);
        }

        private void ClearFlash()
        {
            flashActive = false;
            if (cachedRenderers == null) return;
            for (int i = 0; i < cachedRenderers.Length; i++)
                if (cachedRenderers[i] != null) cachedRenderers[i].SetPropertyBlock(null);
        }
    }
}
