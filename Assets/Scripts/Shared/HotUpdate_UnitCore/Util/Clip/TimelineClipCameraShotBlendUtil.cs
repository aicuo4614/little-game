using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 运镜过渡共用逻辑：解析上/下一条 CameraShot、模板起终点、插值。
/// 起终点均由模板算出（不抓实时机位），正 scrub / 倒 scrub 同一 playTiming 结果一致。
/// </summary>
public static class TimelineClipCameraShotBlendUtil
{
    /// <summary>
    /// 解析目标运镜。优先 <paramref name="preferredClipKey"/>；否则按时间自动找下一条。
    /// </summary>
    public static TimelineClipCameraShotConfig ResolveNextCameraShot(
        IReadOnlyList<TimelineClipConfigBase> clips,
        string preferredClipKey,
        string selfClipKey,
        float blendContentStart,
        float blendContentEnd,
        Func<TimelineClipConfigBase, float> resolveStart = null)
    {
        if (clips == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(preferredClipKey))
        {
            for (var i = 0; i < clips.Count; i++)
            {
                var c = clips[i] as TimelineClipCameraShotConfig;
                if (c != null && c.ClipKey == preferredClipKey)
                {
                    return c;
                }
            }
        }

        TimelineClipCameraShotConfig bestAfterEnd = null;
        var bestAfterEndStart = float.MaxValue;
        TimelineClipCameraShotConfig bestAfterStart = null;
        var bestAfterStartStart = float.MaxValue;

        for (var i = 0; i < clips.Count; i++)
        {
            var shot = clips[i] as TimelineClipCameraShotConfig;
            if (shot == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(selfClipKey) && shot.ClipKey == selfClipKey)
            {
                continue;
            }

            var contentStart = resolveStart?.Invoke(shot) ?? (shot.StartTime + shot.DelayTime);
            if (contentStart + 0.0001f >= blendContentEnd && contentStart < bestAfterEndStart)
            {
                bestAfterEndStart = contentStart;
                bestAfterEnd = shot;
            }

            if (contentStart > blendContentStart + 0.0001f && contentStart < bestAfterStartStart)
            {
                bestAfterStartStart = contentStart;
                bestAfterStart = shot;
            }
        }

        return bestAfterEnd != null ? bestAfterEnd : bestAfterStart;
    }

    /// <summary>
    /// 解析上一条运镜：ContentEnd &lt;= 本条 ContentStart 中结束最晚的一条。
    /// </summary>
    public static TimelineClipCameraShotConfig ResolvePreviousCameraShot(
        IReadOnlyList<TimelineClipConfigBase> clips,
        string selfClipKey,
        float selfContentStart)
    {
        if (clips == null)
        {
            return null;
        }

        TimelineClipCameraShotConfig best = null;
        var bestEnd = float.MinValue;
        for (var i = 0; i < clips.Count; i++)
        {
            var shot = clips[i] as TimelineClipCameraShotConfig;
            if (shot == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(selfClipKey) && shot.ClipKey == selfClipKey)
            {
                continue;
            }

            var contentStart = shot.StartTime + shot.DelayTime;
            var contentEnd = contentStart + Mathf.Max(shot.Duration, 0f);
            if (contentEnd <= selfContentStart + 0.0001f && contentEnd >= bestEnd)
            {
                bestEnd = contentEnd;
                best = shot;
            }
        }

        return best;
    }

    /// <summary>
    /// 求 CameraShot 第 0 帧。环绕忽略 FromCurrentPose；推进用上一条终点（或默认机位）当起点。
    /// </summary>
    public static void EvaluateTargetTemplateStart(
        IReadOnlyList<TimelineClipConfigBase> clips,
        UnitTeamType teamType,
        TimelineClipCameraShotConfig shotConfig,
        UnitMono selfUnit,
        Vector3 fallbackSelfPos,
        Vector3 selfForward,
        IReadOnlyList<UnitMono> targets,
        bool mirror,
        string poseOwnerId,
        out Vector3 worldPos,
        out Quaternion worldRot,
        out float fov)
    {
        ResolveShotEntry(
            clips,
            teamType,
            shotConfig,
            selfUnit,
            fallbackSelfPos,
            selfForward,
            targets,
            poseOwnerId,
            out var hasEntry,
            out var entryPos,
            out var entryRot,
            out var entryFov);
        TimelineClipCameraShotUtil.Evaluate(
            shotConfig,
            0f,
            1f,
            selfUnit,
            fallbackSelfPos,
            selfForward,
            targets,
            mirror,
            hasEntry,
            entryPos,
            entryRot,
            entryFov,
            poseOwnerId,
            out worldPos,
            out worldRot,
            out fov);
    }

    /// <summary>
    /// 求 CameraShot 最后一帧。环绕忽略 FromCurrentPose；推进从上一条终点只改距离。
    /// </summary>
    public static void EvaluateTargetTemplateEnd(
        IReadOnlyList<TimelineClipConfigBase> clips,
        UnitTeamType teamType,
        TimelineClipCameraShotConfig shotConfig,
        UnitMono selfUnit,
        Vector3 fallbackSelfPos,
        Vector3 selfForward,
        IReadOnlyList<UnitMono> targets,
        bool mirror,
        string poseOwnerId,
        out Vector3 worldPos,
        out Quaternion worldRot,
        out float fov)
    {
        ResolveShotEntry(
            clips,
            teamType,
            shotConfig,
            selfUnit,
            fallbackSelfPos,
            selfForward,
            targets,
            poseOwnerId,
            out var hasEntry,
            out var entryPos,
            out var entryRot,
            out var entryFov);
        var dur = Mathf.Max(shotConfig.Duration, 0.0001f);
        TimelineClipCameraShotUtil.Evaluate(
            shotConfig,
            dur,
            dur,
            selfUnit,
            fallbackSelfPos,
            selfForward,
            targets,
            mirror,
            hasEntry,
            entryPos,
            entryRot,
            entryFov,
            poseOwnerId,
            out worldPos,
            out worldRot,
            out fov);
    }

    /// <summary>推进需要起点机位；环绕模板起终点不读 entry。</summary>
    private static void ResolveShotEntry(
        IReadOnlyList<TimelineClipConfigBase> clips,
        UnitTeamType teamType,
        TimelineClipCameraShotConfig shotConfig,
        UnitMono selfUnit,
        Vector3 fallbackSelfPos,
        Vector3 selfForward,
        IReadOnlyList<UnitMono> targets,
        string poseOwnerId,
        out bool hasEntry,
        out Vector3 entryPos,
        out Quaternion entryRot,
        out float entryFov)
    {
        hasEntry = false;
        entryPos = default;
        entryRot = Quaternion.identity;
        entryFov = 0f;
        var needEntry = shotConfig.FromCurrentPose
            || (shotConfig.ShotType == TimelineCameraShotType.Hold && shotConfig.HoldKeepEntryPose);
        if (!needEntry)
        {
            return;
        }

        var contentStart = shotConfig.StartTime + shotConfig.DelayTime;
        ResolveBlendStartPose(
            clips,
            shotConfig.ClipKey,
            contentStart,
            selfUnit,
            fallbackSelfPos,
            selfForward,
            targets,
            teamType,
            poseOwnerId,
            out entryPos,
            out entryRot,
            out entryFov);
        hasEntry = true;
    }

    /// <summary>
    /// 过渡起点：上一条运镜模板终点；没有则用 <see cref="BattleSharedConfig"/>。
    /// </summary>
    public static void ResolveBlendStartPose(
        IReadOnlyList<TimelineClipConfigBase> clips,
        string selfClipKey,
        float selfContentStart,
        UnitMono selfUnit,
        Vector3 fallbackSelfPos,
        Vector3 selfForward,
        IReadOnlyList<UnitMono> targets,
        UnitTeamType teamType,
        string poseOwnerId,
        out Vector3 worldPos,
        out Quaternion worldRot,
        out float fov)
    {
        var prev = ResolvePreviousCameraShot(clips, selfClipKey, selfContentStart);
        if (prev != null)
        {
            var mirror = TimelineTransformUtil.NeedsTeamCameraMirror(prev.MirrorByTeam, teamType);
            EvaluateTargetTemplateEnd(
                clips,
                teamType,
                prev,
                selfUnit,
                fallbackSelfPos,
                selfForward,
                targets,
                mirror,
                poseOwnerId,
                out worldPos,
                out worldRot,
                out fov);
            return;
        }

        TimelineClipCameraShotUtil.GetDefaultBattleCameraPose(out worldPos, out worldRot, out fov);
        var currentMirror = false;
        if (clips != null && !string.IsNullOrEmpty(selfClipKey))
        {
            for (var i = 0; i < clips.Count; i++)
            {
                if (clips[i] is TimelineClipCameraShotConfig shot && shot.ClipKey == selfClipKey)
                {
                    currentMirror = TimelineTransformUtil.NeedsTeamCameraMirror(shot.MirrorByTeam, teamType);
                    break;
                }
            }
        }

        if (currentMirror)
        {
            worldPos = TimelineTransformUtil.MirrorBattleX(worldPos);
            worldRot = TimelineTransformUtil.MirrorBattleRotX(worldRot);
        }
    }

    /// <summary>从起点插值到终点（均已算好的世界位姿）。</summary>
    public static void EvaluateBlend(
        float playTiming,
        float duration,
        Ease blendEase,
        bool blendFov,
        Vector3 entryPos,
        Quaternion entryRot,
        float entryFov,
        Vector3 endPos,
        Quaternion endRot,
        float endFov,
        out Vector3 worldPos,
        out Quaternion worldRot,
        out float fov)
    {
        var t = duration <= 0.0001f ? 1f : Mathf.Clamp01(playTiming / duration);
        var eased = DOVirtual.EasedValue(0f, 1f, t, blendEase);
        worldPos = Vector3.Lerp(entryPos, endPos, eased);
        worldRot = Quaternion.Slerp(entryRot, endRot, eased);
        if (blendFov && endFov > 0f)
        {
            var startFov = entryFov > 0f ? entryFov : endFov;
            fov = Mathf.Lerp(startFov, endFov, eased);
        }
        else
        {
            fov = -1f;
        }
    }
}
