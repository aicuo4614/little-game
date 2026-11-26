using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 3D Animator 确定性时间轴采样轨道（替代 Demo 的 TimelineSpineTrack）：
/// 支持正放与负 Speed 倒放；预览与实战共用同一套采样，退出时恢复 Animator 原状。
/// </summary>
public sealed class TimelineAnimatorTrack
{
    // 同一 Animator 可同时被施法和受击时间轴持有。只有最新轨道采样，最后一个退出才恢复原始状态。
    private sealed class Ownership
    {
        public bool Enabled;
        public float Speed;
        public readonly List<TimelineAnimatorTrack> Tracks = new List<TimelineAnimatorTrack>();
    }
    private static readonly Dictionary<Animator, Ownership> Owners = new Dictionary<Animator, Ownership>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Owners.Clear(); }
    private Ownership ownership;
    private readonly Animator animator;
    private readonly AnimationClip clip;
    private readonly int stateHash;
    private readonly float stateSpeed;
    private readonly float wallDuration;
    private readonly bool loop;
    private bool released;

    public float WallDuration => wallDuration;

    public TimelineAnimatorTrack(Animator animator, string stateOrClipName, float speed = 1f, bool loop = false)
    {
        this.animator = animator;
        this.loop = loop;
        stateSpeed = Mathf.Abs(speed) > 0.0001f ? speed : 1f;
        if (!animator)
        {
            return;
        }

        if (!Owners.TryGetValue(animator, out ownership))
        {
            ownership = new Ownership { Enabled = animator.enabled, Speed = animator.speed };
            Owners.Add(animator, ownership);
        }
        ownership.Tracks.Add(this);
        stateHash = Animator.StringToHash(stateOrClipName ?? string.Empty);
        clip = FindClip(animator, stateOrClipName);
        if (clip != null)
        {
            wallDuration = clip.length / Mathf.Abs(stateSpeed);
        }
    }

    public static AnimationClip FindClip(Animator animator, string stateOrClipName)
    {
        if (!animator || animator.runtimeAnimatorController == null || string.IsNullOrWhiteSpace(stateOrClipName))
        {
            return null;
        }

        var clips = animator.runtimeAnimatorController.animationClips;
        if (clips == null || clips.Length == 0)
        {
            return null;
        }

        for (var i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && string.Equals(clips[i].name, stateOrClipName, StringComparison.OrdinalIgnoreCase))
            {
                return clips[i];
            }
        }

        // 兼容形如 Role_Attack / Role_Idle 后缀匹配
        var suffix = "_" + stateOrClipName;
        for (var i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && clips[i].name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return clips[i];
            }
        }

        return null;
    }

    public static float MeasureDuration(Animator animator, string stateOrClipName, float speed = 1f)
    {
        var found = FindClip(animator, stateOrClipName);
        if (found == null)
        {
            return 0f;
        }

        var absSpeed = Mathf.Abs(speed) > 0.0001f ? Mathf.Abs(speed) : 1f;
        return found.length / absSpeed;
    }

    public void Sample(float time)
    {
        if (released || !animator)
        {
            return;
        }

        if (ownership.Tracks[ownership.Tracks.Count - 1] != this) return;
        time = Mathf.Max(0f, time);
        if (clip != null && clip.length > 0.0001f)
        {
            var scaled = time * Mathf.Abs(stateSpeed);
            float clipTime;
            if (loop)
            {
                clipTime = scaled % clip.length;
                if (stateSpeed < 0f)
                {
                    clipTime = clip.length - clipTime;
                }
            }
            else
            {
                var clamped = Mathf.Clamp(scaled, 0f, clip.length);
                clipTime = stateSpeed < 0f ? clip.length - clamped : clamped;
            }

            if (animator.enabled)
            {
                animator.enabled = false;
            }

            clip.SampleAnimation(animator.gameObject, clipTime);
            return;
        }

        if (animator.runtimeAnimatorController != null && animator.HasState(0, stateHash))
        {
            if (!animator.enabled)
            {
                animator.enabled = true;
            }

            animator.speed = 0f;
            var norm = wallDuration > 0.0001f ? Mathf.Clamp01(time / wallDuration) : 0f;
            animator.Play(stateHash, 0, norm);
            animator.Update(0f);
        }
    }

    public void Release()
    {
        if (released || ownership == null) return;
        released = true;
        ownership.Tracks.Remove(this);
        if (ownership.Tracks.Count == 0)
        {
            Owners.Remove(animator);
            if (animator)
            {
                animator.enabled = ownership.Enabled;
                animator.speed = ownership.Speed;
            }
        }
    }
}
