using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 运行时单次技能时间轴实例（对齐 Demo BattleSkillTimeline）：
    /// 复用 HotUpdate_UnitCore 的共享 TimelinePlayback / TimelinePlayer，驱动 3D Animator、弹道、特效、受击、震屏与场景压暗，
    /// 并通过 BattleUnit.StateControl.BeginSkill / CompleteSkill 纳入单位有限状态机管理。
    /// </summary>
    public sealed class BattleSkillTimeline
    {
        private readonly BattleView view;
        private readonly TimelineConfig config;
        private readonly int skillInstanceId;
        private readonly BattleUnit caster;
        private readonly BattleUnit target;
        private readonly Vector3 fallbackPoint;

        private TimelinePlayback playback;
        private int casterSkillHandle;
        private float elapsed;
        private float maxDuration;
        private bool stopped;

        public int SkillInstanceId => skillInstanceId;
        public string TimelineId => config?.TimelineId ?? string.Empty;
        public bool IsFinished => stopped;
        public object Diagnostics => new { skillInstanceId, timelineId = TimelineId, elapsed,
            duration = maxDuration, visualImpactAt = playback?.FirstImpactTime, stopped };

        public BattleSkillTimeline(
            BattleView view,
            TimelineConfig config,
            int skillInstanceId,
            BattleUnit caster,
            BattleUnit target,
            Vector3 fallbackPoint)
        {
            this.view = view;
            this.config = config;
            this.skillInstanceId = skillInstanceId;
            this.caster = caster;
            this.target = target;
            this.fallbackPoint = fallbackPoint;
        }

        public bool Start()
        {
            if (config == null || view == null || view.IsTornDown)
            {
                stopped = true;
                return false;
            }

            BattleUnitTimelineActor casterActor = caster != null && !caster.IsEnded ? caster.ToTimelineActor() : null;
            BattleUnitTimelineActor targetActor = target != null && !target.IsEnded ? target.ToTimelineActor() : null;
            GameObject anchorGo = null;
            if (casterActor == null)
            {
                anchorGo = new GameObject("TimelineAnchor_" + TimelineId);
                anchorGo.transform.SetParent(view.WorldRoot, false);
                anchorGo.transform.position = fallbackPoint;
                casterActor = new BattleUnitTimelineActor(
                    anchorGo.transform,
                    anchorGo.transform,
                    null,
                    () => view.CameraControl.Camera != null ? view.CameraControl.Camera.transform.right : Vector3.right,
                    _ => anchorGo.transform,
                    fallbackPoint);
            }

            var casterList = new List<BattleUnitTimelineActor> { casterActor };
            var targetList = targetActor != null
                ? new List<BattleUnitTimelineActor> { targetActor }
                : new List<BattleUnitTimelineActor> { casterActor };

            var context = new TimelinePlaybackContext
            {
                IncrementalEffects = true,
                Resolve = (clip, subject, bindingKey) =>
                {
                    return subject switch
                    {
                        TimelineUnitSubjectType.Caster or TimelineUnitSubjectType.EffectSource => casterList,
                        TimelineUnitSubjectType.CastTargets or TimelineUnitSubjectType.EffectTargets => targetList,
                        _ => casterList,
                    };
                },
                AllActors = () =>
                {
                    var all = new List<BattleUnitTimelineActor>();
                    foreach (var kv in view.UnitsControl.Units)
                    {
                        if (!kv.Value.IsEnded) all.Add(kv.Value.ToTimelineActor());
                    }
                    return all;
                },
                CanPlayHit = (_, actor) =>
                {
                    var unit = view.UnitsControl.Units.Values.FirstOrDefault(u => u.Transform == actor.Root);
                    return unit != null && !unit.IsDead && !unit.StateControl.IsInSkill
                        && unit.StateControl.CurrentStateType != BattleUnitStateType.Controlled;
                },
                SpawnEffect = view.EffectControl.SpawnEffect,
                DespawnEffect = view.EffectControl.DespawnEffect,
                CameraRoot = view.CameraControl.CameraRoot,
                Camera = view.CameraControl.Camera,
                ShakeTransform = view.CameraControl.ShakeTransform,
                HideBattleUI = _ => () => { },
                VisibilityEnvironment = () => view.SceneControl.Environment != null ? view.SceneControl.Environment.transform : null,
            };

            try
            {
                playback = new TimelinePlayback(config, context, OnHitReached);
                maxDuration = Mathf.Max(0.15f, playback.Player.Duration);
            }
            catch (Exception ex)
            {
                GameDebug.LogWarning("BattleSkillTimeline", $"创建 TimelinePlayback 失败 ({TimelineId}): {ex.Message}");
                if (anchorGo != null) UnityEngine.Object.Destroy(anchorGo);
                stopped = true;
                return false;
            }

            Action stopAction = () =>
            {
                StopInternal();
                if (anchorGo != null)
                {
                    UnityEngine.Object.Destroy(anchorGo);
                    anchorGo = null;
                }
            };

            bool hasAnimatorClip = config.Clips.Exists(c => c is TimelineClipPlayAnimatorConfig);
            if (caster != null && !caster.IsEnded && !caster.IsDead && hasAnimatorClip)
            {
                var animClip = config.Clips.Find(c => c is TimelineClipPlayAnimatorConfig) as TimelineClipPlayAnimatorConfig;
                casterSkillHandle = caster.StateControl.BeginSkill(
                    play: () =>
                    {
                        caster.AnimatorControl.SetTimelineControlled(true, animClip?.ParamName ?? "Attack");
                        playback.Player.Evaluate(0f);
                    },
                    stopPlayback: stopAction);
                if (casterSkillHandle <= 0)
                {
                    stopAction();
                    return false;
                }
            }
            else
            {
                playback.Player.Evaluate(0f);
                anchorCleanup = () =>
                {
                    if (anchorGo != null)
                    {
                        UnityEngine.Object.Destroy(anchorGo);
                        anchorGo = null;
                    }
                };
            }

            return true;
        }

        private Action anchorCleanup;

        public void Tick(float deltaTime)
        {
            if (stopped || playback == null) return;
            elapsed += deltaTime;
            try
            {
                playback.Player.Evaluate(elapsed);
            }
            catch (Exception ex)
            {
                GameDebug.LogWarning("BattleSkillTimeline", $"推进 TimelinePlayback 异常 ({TimelineId}): {ex.Message}");
                Stop();
                return;
            }

            if (elapsed >= maxDuration)
            {
                Stop();
            }
        }

        public void Stop()
        {
            if (stopped) return;
            if (casterSkillHandle > 0 && caster != null && !caster.IsEnded)
            {
                int handle = casterSkillHandle;
                casterSkillHandle = 0;
                caster.StateControl.CompleteSkill(handle);
            }
            else
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            if (stopped) return;
            stopped = true;
            casterSkillHandle = 0;

            try
            {
                playback?.Player.Stop();
            }
            catch (Exception ex)
            {
                GameDebug.LogWarning("BattleSkillTimeline", $"停止 TimelinePlayback 异常 ({TimelineId}): {ex.Message}");
            }
            playback = null;

            anchorCleanup?.Invoke();
            anchorCleanup = null;

            if (caster != null && !caster.IsEnded)
            {
                caster.AnimatorControl.SetTimelineControlled(false);
                caster.StateControl.RefreshPresentation();
            }


        }

        private void OnHitReached(TimelineClipPlayHitConfig hitClip)
        {
            if (target != null && !target.IsEnded && !target.IsDead)
            {
                target.GraphicControl.TriggerHitFlash(0.12f);
            }
        }
    }
}
