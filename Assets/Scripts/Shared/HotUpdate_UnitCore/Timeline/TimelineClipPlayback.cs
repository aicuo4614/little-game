using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>PlayAnimator 和 PlayHit 的共用 3D 表现，资源实例的所有权由调用方提供。</summary>
public sealed class TimelineClipPlayback
{
    private readonly Animator caster;
    private readonly Animator target;
    private readonly Transform hitAnchor;
    private readonly Func<string, Transform, GameObject> spawnEffect;
    private readonly Action<GameObject> recycleEffect;
    private readonly Dictionary<TimelineClipConfigBase, TimelineAnimatorTrack> tracks =
        new Dictionary<TimelineClipConfigBase, TimelineAnimatorTrack>();
    private readonly Dictionary<TimelineClipPlayHitConfig, GameObject> effects =
        new Dictionary<TimelineClipPlayHitConfig, GameObject>();
    private readonly Dictionary<TimelineClipPlayHitConfig, TimelineEffectPlayback> effectPlayers =
        new Dictionary<TimelineClipPlayHitConfig, TimelineEffectPlayback>();
    private readonly BattleUnitTimelineActor hitActor;
    private readonly BattleUnitTimelineActor effectSource;
    private readonly Func<string, EffectPointLightData> effectLight;
    private readonly bool incrementalEffects;

    public TimelineClipPlayback(
        Animator caster,
        Animator target,
        Transform hitAnchor,
        Func<string, Transform, GameObject> spawnEffect,
        Action<GameObject> recycleEffect,
        BattleUnitTimelineActor hitActor = null,
        Func<string, EffectPointLightData> effectLight = null,
        BattleUnitTimelineActor effectSource = null,
        bool incrementalEffects = false)
    {
        this.caster = caster;
        this.target = target;
        this.hitAnchor = hitAnchor;
        this.hitActor = hitActor;
        this.effectSource = effectSource;
        this.incrementalEffects = incrementalEffects;
        this.effectLight = effectLight;
        this.spawnEffect = spawnEffect ?? throw new ArgumentNullException(nameof(spawnEffect));
        this.recycleEffect = recycleEffect ?? throw new ArgumentNullException(nameof(recycleEffect));
    }

    public void Sample(TimelineClipConfigBase config, float time)
    {
        switch (config)
        {
            case TimelineClipPlayAnimatorConfig anim:
            {
                SampleAnimator(anim, caster, anim.ParamName, anim.AnimatorStateSpeed, false, time);
                break;
            }
            case TimelineClipPlayHitConfig hit:
            {
                var hitAnimName = !string.IsNullOrEmpty(hit.AnimatorParamName)
                    ? hit.AnimatorParamName
                    : hit.SpineAnimationName;
                var hitDelay = hit.AnimatorDelayTime > 0f ? hit.AnimatorDelayTime : hit.SpineAnimationDelay;
                if (!string.IsNullOrEmpty(hitAnimName) && time >= hitDelay)
                {
                    SampleAnimator(hit, target, hitAnimName, 1f, false, time - hitDelay);
                }

                SampleEffect(hit, time);
                break;
            }
        }
    }

    public void Exit(TimelineClipConfigBase config)
    {
        if (tracks.Remove(config, out var track))
        {
            track?.Release();
        }

        if (config is TimelineClipPlayHitConfig hit && effects.Remove(hit, out var effect))
        {
            if (effectPlayers.Remove(hit, out var player))
            {
                player.Release();
            }

            if (effect)
            {
                recycleEffect(effect);
            }
        }
    }

    private void SampleAnimator(
        TimelineClipConfigBase config,
        Animator animator,
        string paramName,
        float speed,
        bool loop,
        float time)
    {
        if (!tracks.TryGetValue(config, out var track))
        {
            if (animator && !string.IsNullOrEmpty(paramName))
            {
                track = new TimelineAnimatorTrack(animator, paramName, speed, loop);
            }

            tracks.Add(config, track);
        }

        track?.Sample(time);
    }

    private void SampleEffect(TimelineClipPlayHitConfig hit, float time)
    {
        if (!hitAnchor || string.IsNullOrEmpty(hit.EffectPath))
        {
            return;
        }

        if (!effects.TryGetValue(hit, out var effect))
        {
            effect = spawnEffect(hit.EffectPath, hitAnchor);
            effects.Add(hit, effect);
            if (!effect)
            {
                return;
            }

            if (hitActor is not null && hit.UnitNodePointData is TimelineClipPointData point)
            {
                TimelineActorTransform.ApplyHit(effect.transform, hitActor, point, effectSource);
            }
            else
            {
                effect.transform.SetParent(hitAnchor, false);
                effect.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            effectPlayers.Add(hit, new TimelineEffectPlayback(effect, effectLight?.Invoke(hit.EffectPath), incrementalEffects));
        }

        if (!effect)
        {
            return;
        }

        effectPlayers[hit].Sample(time, hit.Duration);
    }
}
