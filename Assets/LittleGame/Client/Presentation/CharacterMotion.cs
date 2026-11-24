using System;
using System.Linq;
using UnityEngine;

namespace LittleGame.Client
{
    /// <summary>Legacy骨架表现适配。动作完成不回写规则，避免演出影响结算。</summary>
    public sealed class CharacterMotion : MonoBehaviour
    {
        private Animation animationPlayer;
        private Vector3 previous;
        private bool hasAttack;
        private bool dying;
        private float deathTime;
        private float attackSpeed = 1.5f;
        public string CurrentAction { get; private set; }
        public bool DeathFinished => dying && deathTime >= (animationPlayer["Death"]?.length ?? 0);
        public float ActionTime => animationPlayer != null && CurrentAction != null && animationPlayer[CurrentAction] != null ? animationPlayer[CurrentAction].time : 0;
        public void Configure(UnitAppearance appearance)
        {
            animationPlayer = GetComponent<Animation>() ?? gameObject.AddComponent<Animation>();
            animationPlayer.enabled = true;
            attackSpeed = appearance.AttackSpeed;
            AddClip("Idle", appearance.Idle, true); AddClip("Walk", appearance.Move, true);
            hasAttack = AddClip("Attack", appearance.Attack, false);
            AddClip("Hit", appearance.Hit, false); AddClip("Death", appearance.Death, false); AddClip("Slam", appearance.Slam, false);
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
            if (!alive && !dying) { dying = true; deathTime = 0; Play("Death"); }
            else if (alive && dying) { dying = false; deathTime = 0; Play("Idle"); }
        }
        public void Hit() { if (!dying && animationPlayer != null && !animationPlayer.IsPlaying("Attack")) Play("Hit"); }
        public void Slam() { if (!dying) Play("Slam"); }
        public void Configure(int kind)
        {
            animationPlayer = GetComponent<Animation>() ?? gameObject.AddComponent<Animation>(); animationPlayer.enabled = true;
            Add("Idle", "Rig_Medium_General", "Idle_A", true);
            Add("Walk", "Rig_Medium_MovementBasic", "Walking_A", true);
            hasAttack = Add("Attack", kind == 0 ? "Rig_Medium_CombatMelee" : "Rig_Medium_CombatRanged", kind == 0 ? "Melee_1H_Attack_Chop" : kind == 1 ? "Ranged_Bow_Release" : "Ranged_Magic_Spellcasting", false);
            if (animationPlayer["Idle"] != null) { animationPlayer.Play("Idle"); animationPlayer.Sample(); }
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
        public void Apply(bool paused)
        {
            if (animationPlayer == null || !isActiveAndEnabled) return;
            foreach (AnimationState state in animationPlayer) state.speed = paused ? 0 : state.name == "Attack" ? attackSpeed : 1;
            // 暂停时保持当前动作，不让待机/行走切换重新启动播放时钟。
            if (paused) { previous = transform.position; return; }
            if (dying) { if (!paused) deathTime += Time.unscaledDeltaTime; return; }
            var delta = transform.position - previous;
            bool moving = delta.x * delta.x + delta.z * delta.z > .000001f; previous = transform.position;
            if (animationPlayer.IsPlaying("Attack") || animationPlayer.IsPlaying("Hit") || animationPlayer.IsPlaying("Slam")) return;
            string desired = moving && animationPlayer["Walk"] != null ? "Walk" : "Idle";
            if (animationPlayer[desired] != null && !animationPlayer.IsPlaying(desired)) { CurrentAction = desired; animationPlayer.CrossFade(desired, .1f); }
        }
    }
}
