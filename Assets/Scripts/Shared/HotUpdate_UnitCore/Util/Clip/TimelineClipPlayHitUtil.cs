using UnityEngine;

/// <summary>PlayHit Clip 共用：把受击特效挂到目标节点并应用点位变换（含重置旋转/缩放、不跟随语义）。</summary>
public static class TimelineClipPlayHitUtil
{
    /// <summary>
    /// 按 <paramref name="pointData"/> 把特效摆到目标单位节点上。
    /// 与 PlayEffect 相同：走 <see cref="TimelineTransformUtil.ApplyEffectTransform"/>。
    /// </summary>
    public static void ApplyEffectTransform(
        Transform effectTransform,
        TimelineClipPointData pointData,
        UnitMono targetUnit,
        Vector3 originPos)
    {
        TimelineTransformUtil.GetTransformationFromUnitNodePointData(
            originPos,
            targetUnit,
            pointData,
            out var position,
            out var rotation,
            out var scale);

        var parent = TimelineTransformUtil.GetParentFromUnitNodePointData(pointData, targetUnit);
        TimelineTransformUtil.ApplyEffectTransform(
            effectTransform,
            pointData,
            parent,
            position,
            rotation,
            scale,
            targetUnit);
    }

    /// <summary>
    /// 挂载：仅「重置旋转 / 重置缩放」需每帧回写；未挂载：未勾「不跟随」则每帧跟节点世界位姿。
    /// </summary>
    public static bool NeedPerFrameApply(TimelineClipPointData pointData)
    {
        if (pointData == null)
        {
            return false;
        }

        if (pointData.NeedSetParent)
        {
            return pointData.UseOriginalRotation || pointData.UseOriginalScale;
        }

        return !pointData.DontFollow;
    }
}
