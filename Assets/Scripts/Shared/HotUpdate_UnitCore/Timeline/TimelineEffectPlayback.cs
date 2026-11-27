using System.Collections.Generic;
using UnityEngine;

/// <summary>两端一致的特效粒子与 Animator 绝对时间采样；资源归宿主所有。</summary>
public sealed class TimelineEffectPlayback
{
    public GameObject Instance { get; }
    private readonly List<ParticleSystem> roots = new List<ParticleSystem>();
    private readonly Animator[] animators;
    private readonly List<int> states = new List<int>();
    private readonly List<float> animatorSpeeds = new List<float>();
    private readonly EffectPointLightData lightData;
    private readonly Light pointLight;
    private readonly bool incremental;
    private float previousTime = -1f;

    public TimelineEffectPlayback(GameObject instance, EffectPointLightData lightData = null, bool incremental = false)
    {
        Instance = instance;
        this.incremental = incremental;
        this.lightData = lightData;
        pointLight = EffectPointLightUtil.FindPointLight(instance.transform);
        var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (var i = 0; i < systems.Length; i++)
        {
            var system = systems[i];
            if (!system.gameObject.activeInHierarchy) continue;
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.useUnscaledTime = false;
            main.playOnAwake = false;
            system.useAutoRandomSeed = false;
            if (system.randomSeed == 0)
            {
                system.randomSeed = 1;
            }

            system.Pause(false);
            var parent = system.transform.parent;
            var child = false;
            while (parent && parent != instance.transform.parent)
            {
                if (parent.GetComponent<ParticleSystem>())
                {
                    child = true;
                    break;
                }

                parent = parent.parent;
            }

            if (!child)
            {
                roots.Add(system);
            }
        }

        animators = instance.GetComponentsInChildren<Animator>(true);
        for (var i = 0; i < animators.Length; i++)
        {
            animators[i].Rebind();
            animators[i].Update(0f);
            states.Add(animators[i].GetCurrentAnimatorStateInfo(0).fullPathHash);
            animatorSpeeds.Add(animators[i].speed);
            animators[i].speed = 0f;
        }
    }

    public void Sample(float time, float duration)
    {
        time = Mathf.Max(0f, time);
        bool restart = !incremental || previousTime < 0f || time < previousTime;
        float delta = restart ? time : time - previousTime;
        for (var i = 0; i < roots.Count; i++)
        {
            if (restart || delta > 0f) roots[i].Simulate(delta, true, restart, !incremental);
        }
        previousTime = time;

        for (var i = 0; i < animators.Length; i++)
        {
            animators[i].Play(states[i], 0, time / Mathf.Max(duration, 0.01f));
            animators[i].Update(0f);
        }

        if (pointLight && lightData is not null)
        {
            EffectPointLightUtil.ApplyAtTime(pointLight, lightData, time);
        }
    }

    public void Release()
    {
        for (var i = 0; i < animators.Length; i++)
        {
            animators[i].speed = animatorSpeeds[i];
        }
    }
}
