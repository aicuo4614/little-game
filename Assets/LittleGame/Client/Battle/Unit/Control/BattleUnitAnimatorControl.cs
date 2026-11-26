using System;
using System.Collections.Generic;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 3D Animator 底层播放控件（对齐 Demo BattleUnitAnimatorControl，以原生 3D Animator 替代 Spine）：
    /// 负责 Idle / Walk / Attack / Hit / Death / Slam 状态播放、暂停/冻结/定帧速率管理与状态时长查询。
    /// </summary>
    public sealed class BattleUnitAnimatorControl : BattleUnitControlBase
    {
        private Animator animator;
        private readonly Dictionary<string, float> clipLengths = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> stateHashes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private string currentAction = "Idle";
        private float actionTime;
        private float baseSpeed = 1f;
        private float speedMultiplier = 1f;
        private bool paused;
        private bool frozen;
        private bool hitStopped;
        private bool timelineControlled;

        public Animator Animator => animator;
        public string CurrentAction => currentAction;
        public float ActionTime => actionTime;
        public bool IsHitStopped => hitStopped;
        public bool HasValidController => animator != null && animator.runtimeAnimatorController != null;

        public bool IsPlaying
        {
            get
            {
                if (animator == null || !animator.gameObject.activeInHierarchy)
                {
                    return false;
                }

                if (timelineControlled)
                {
                    return true;
                }

                return animator.enabled && HasValidController;
            }
        }

        public override void Init(BattleUnit owner)
        {
            base.Init(owner);
            animator = owner.ModelControl.Animator;
            CacheControllerClips();
        }

        public override void Start()
        {
            PlayIdle();
        }

        public override void Tick(float deltaTime)
        {
            if (animator == null)
            {
                return;
            }

            if (!paused && !frozen && !hitStopped)
            {
                actionTime += deltaTime * Mathf.Max(0.1f, baseSpeed * speedMultiplier);
            }

            if (!timelineControlled)
            {
                ApplyEffectiveSpeed();
            }
        }

        public void SetTimelineControlled(bool controlled, string actionName = null)
        {
            timelineControlled = controlled;
            if (controlled && !string.IsNullOrEmpty(actionName))
            {
                currentAction = actionName;
                actionTime = 0f;
            }
            else if (!controlled && animator != null)
            {
                animator.enabled = true;
                ApplyEffectiveSpeed();
            }
        }

        public void SetPaused(bool value)
        {
            paused = value;
            if (!timelineControlled)
            {
                ApplyEffectiveSpeed();
            }
        }

        public void SetFrozen(bool value)
        {
            frozen = value;
            if (!timelineControlled)
            {
                ApplyEffectiveSpeed();
            }
        }

        public void SetHitStopped(bool value)
        {
            hitStopped = value;
            if (!timelineControlled)
            {
                ApplyEffectiveSpeed();
            }
        }

        public void SetBattleSpeed(float multiplier)
        {
            speedMultiplier = Mathf.Clamp(multiplier, 0.25f, 4f);
            if (!timelineControlled)
            {
                ApplyEffectiveSpeed();
            }
        }

        public void PlayIdle()
        {
            PlayState("Idle", 1f, restartIfSame: false);
        }

        public void PlayWalk(float walkSpeed = 1f)
        {
            PlayState("Walk", walkSpeed, restartIfSame: false);
        }

        public void SetWalkSpeed(float walkSpeed)
        {
            baseSpeed = Mathf.Clamp(walkSpeed, 0.5f, 2f);
            if (!timelineControlled)
            {
                ApplyEffectiveSpeed();
            }
        }

        public float PlayAttack(float speed = 1f) => PlayState("Attack", speed, restartIfSame: true);
        public float PlayHit(float speed = 1f) => PlayState("Hit", speed, restartIfSame: true);
        public float PlaySlam(float speed = 1f) => PlayState("Slam", speed, restartIfSame: true);
        public float PlayDeath(float speed = 1f) => PlayState("Death", speed, restartIfSame: true);

        public float PlayState(string stateName, float speed = 1f, bool restartIfSame = true)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                stateName = "Idle";
            }

            if (!restartIfSame && string.Equals(currentAction, stateName, StringComparison.OrdinalIgnoreCase) && !timelineControlled)
            {
                baseSpeed = Mathf.Max(0.1f, speed);
                ApplyEffectiveSpeed();
                return GetStateLength(stateName);
            }

            currentAction = stateName;
            actionTime = 0f;
            baseSpeed = Mathf.Max(0.1f, speed);

            if (animator != null && HasValidController && animator.gameObject.activeInHierarchy)
            {
                animator.enabled = true;
                ApplyEffectiveSpeed();
                int hash = GetHash(stateName);
                if (animator.HasState(0, hash))
                {
                    if (restartIfSame)
                    {
                        animator.Play(hash, 0, 0f);
                    }
                    else
                    {
                        animator.CrossFadeInFixedTime(hash, 0.08f, 0);
                    }
                }
                else
                {
                    int idleHash = GetHash("Idle");
                    if (animator.HasState(0, idleHash))
                    {
                        animator.Play(idleHash, 0, 0f);
                    }
                }
            }

            return GetStateLength(stateName);
        }

        public float GetStateLength(string stateName)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                return 0.5f;
            }

            if (clipLengths.TryGetValue(stateName, out var len) && len > 0.01f)
            {
                return len;
            }

            return stateName.Equals("Death", StringComparison.OrdinalIgnoreCase) ? 0.55f : 0.48f;
        }

        public void Stop()
        {
            if (animator != null)
            {
                animator.speed = 0f;
            }
        }

        private void ApplyEffectiveSpeed()
        {
            if (animator == null || !animator.enabled)
            {
                return;
            }

            animator.speed = (paused || frozen || hitStopped) ? 0f : baseSpeed * speedMultiplier;
        }

        private void CacheControllerClips()
        {
            clipLengths.Clear();
            if (!HasValidController)
            {
                return;
            }

            var clips = animator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c == null) continue;
                clipLengths[c.name] = c.length;
                // 映射常见标准状态名（如 Archer_Attack -> Attack）
                MapKeyword(c.name, "Idle", c.length);
                MapKeyword(c.name, "Walk", c.length);
                MapKeyword(c.name, "Attack", c.length);
                MapKeyword(c.name, "Hit", c.length);
                MapKeyword(c.name, "Death", c.length);
                MapKeyword(c.name, "Die", c.length, "Death");
                MapKeyword(c.name, "Slam", c.length);
            }
        }

        private void MapKeyword(string clipName, string keyword, float length, string targetKey = null)
        {
            string key = targetKey ?? keyword;
            if (!clipLengths.ContainsKey(key) && clipName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                clipLengths[key] = length;
            }
        }

        private int GetHash(string stateName)
        {
            if (!stateHashes.TryGetValue(stateName, out var hash))
            {
                hash = Animator.StringToHash(stateName);
                stateHashes[stateName] = hash;
            }
            return hash;
        }
    }
}
