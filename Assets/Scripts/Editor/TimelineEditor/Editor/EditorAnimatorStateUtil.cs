using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 编辑器侧按 AnimatorController <b>State 名</b>解析 Motion / 时长 / Speed。
/// 不能用 <c>runtimeAnimatorController.animationClips[].name</c> 对齐 ParamName：
/// Roco 等资源 State 叫 Skill_5，FBX Clip 却是 Gra_xxx|Fight_Skill_1_End。
/// </summary>
public static class EditorAnimatorStateUtil
{
    /// <summary>State.speed 接近 0 时当作 1，避免时长除零。</summary>
    const float MinAbsSpeed = 0.0001f;

    /// <summary>收集 Controller 各层 State 名（含子状态机），供下拉与 Play 哈希一致。</summary>
    public static void CopyStateNames(RuntimeAnimatorController runtime, List<string> dest)
    {
        dest.Clear();
        var controller = GetAnimatorController(runtime);
        if (controller == null)
        {
            if (runtime == null)
            {
                return;
            }

            var clips = runtime.animationClips;
            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && !dest.Contains(clips[i].name))
                {
                    dest.Add(clips[i].name);
                }
            }

            return;
        }

        for (var i = 0; i < controller.layers.Length; i++)
        {
            CollectStateNames(controller.layers[i].stateMachine, dest);
        }
    }

    /// <summary>按 State 名取绑定的 AnimationClip（含 Override）；找不到再回退按 Clip 资源名匹配。</summary>
    public static AnimationClip FindClipByStateName(RuntimeAnimatorController runtime, string stateName)
    {
        if (runtime == null || string.IsNullOrEmpty(stateName))
        {
            return null;
        }

        var overrideController = runtime as AnimatorOverrideController;
        var state = FindState(runtime, stateName);
        if (state != null)
        {
            return ResolveMotionClip(state.motion, overrideController);
        }

        var clips = runtime.animationClips;
        for (var i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && clips[i].name == stateName)
            {
                return clips[i];
            }
        }

        return null;
    }

    /// <summary>
    /// 墙钟时长 = Clip.length / |State.speed|。
    /// Speed=2 则条长减半；Speed=-1 倒放，条长仍按绝对值。
    /// </summary>
    public static float GetStateLength(RuntimeAnimatorController runtime, string stateName)
    {
        var clip = FindClipByStateName(runtime, stateName);
        if (clip == null)
        {
            return 0f;
        }

        var speed = GetStateSpeed(runtime, stateName);
        var absSpeed = Mathf.Abs(speed);
        if (absSpeed < MinAbsSpeed)
        {
            absSpeed = 1f;
        }

        return clip.length / absSpeed;
    }

    /// <summary>Animator 状态上的 Speed；找不到 State 时返回 1。</summary>
    public static float GetStateSpeed(RuntimeAnimatorController runtime, string stateName)
    {
        var state = FindState(runtime, stateName);
        if (state == null)
        {
            return 1f;
        }

        return state.speed;
    }

    /// <summary>按 State 名找 AnimatorState（含子状态机）。</summary>
    public static AnimatorState FindState(RuntimeAnimatorController runtime, string stateName)
    {
        if (runtime == null || string.IsNullOrEmpty(stateName))
        {
            return null;
        }

        var controller = GetAnimatorController(runtime);
        if (controller == null)
        {
            return null;
        }

        for (var i = 0; i < controller.layers.Length; i++)
        {
            var state = FindStateInStateMachine(controller.layers[i].stateMachine, stateName);
            if (state != null)
            {
                return state;
            }
        }

        return null;
    }

    static AnimatorController GetAnimatorController(RuntimeAnimatorController runtime)
    {
        if (runtime is AnimatorController controller)
        {
            return controller;
        }

        if (runtime is AnimatorOverrideController overrideController)
        {
            return overrideController.runtimeAnimatorController as AnimatorController;
        }

        return null;
    }

    static void CollectStateNames(AnimatorStateMachine stateMachine, List<string> dest)
    {
        var states = stateMachine.states;
        for (var i = 0; i < states.Length; i++)
        {
            var name = states[i].state.name;
            if (!string.IsNullOrEmpty(name) && !dest.Contains(name))
            {
                dest.Add(name);
            }
        }

        var children = stateMachine.stateMachines;
        for (var i = 0; i < children.Length; i++)
        {
            CollectStateNames(children[i].stateMachine, dest);
        }
    }

    static AnimatorState FindStateInStateMachine(AnimatorStateMachine stateMachine, string stateName)
    {
        var states = stateMachine.states;
        for (var i = 0; i < states.Length; i++)
        {
            if (states[i].state.name == stateName)
            {
                return states[i].state;
            }
        }

        var children = stateMachine.stateMachines;
        for (var i = 0; i < children.Length; i++)
        {
            var state = FindStateInStateMachine(children[i].stateMachine, stateName);
            if (state != null)
            {
                return state;
            }
        }

        return null;
    }

    static AnimationClip ResolveMotionClip(Motion motion, AnimatorOverrideController overrideController)
    {
        if (motion == null)
        {
            return null;
        }

        if (motion is AnimationClip clip)
        {
            if (overrideController != null)
            {
                var overridden = overrideController[clip];
                return overridden != null ? overridden : clip;
            }

            return clip;
        }

        if (motion is BlendTree tree)
        {
            return ResolveBlendTreeLongestClip(tree, overrideController);
        }

        return null;
    }

    static AnimationClip ResolveBlendTreeLongestClip(BlendTree tree, AnimatorOverrideController overrideController)
    {
        AnimationClip best = null;
        var bestLength = 0f;
        var children = tree.children;
        for (var i = 0; i < children.Length; i++)
        {
            var childClip = ResolveMotionClip(children[i].motion, overrideController);
            if (childClip == null)
            {
                continue;
            }

            if (childClip.length >= bestLength)
            {
                bestLength = childClip.length;
                best = childClip;
            }
        }

        return best;
    }
}
