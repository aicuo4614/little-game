using System.Collections.Generic;
using UnityEngine;

/// <summary>基于演出角色的挂点变换，编辑器和实战不再各自实现特效挂载。</summary>
public static class TimelineActorTransform
{
    /// <summary>按首个单位或群体中心计算位置、旋转、缩放。</summary>
    public static void Evaluate(List<BattleUnitTimelineActor> actors, TimelineClipPointData point,
        Vector3 origin, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        rotation = Quaternion.Euler(point.OffsetEuler);
        scale = point.OffsetScale;
        if (point.OffsetNodeType == UnitNodeType.World)
        {
            position = point.OffsetPos;
            return;
        }
        var actor = actors[0];
        if (point.OffsetNodeType == UnitNodeType.GroupCenter)
        {
            var center = Vector3.zero;
            for (var i = 0; i < actors.Count; i++)
            {
                center += actors[i].Root.position;
            }
            center /= actors.Count;
            position = TimelineMathUtil.GetTeamCenterByOffset(center, origin, point.OffsetPos);
            var direction = center - position;
            rotation = (direction.sqrMagnitude > 0.000001f ? Quaternion.LookRotation(direction) : Quaternion.identity) * rotation;
        }
        else if (point.OffsetNodeType == UnitNodeType.UnitOriginPos)
        {
            position = actor.InitialPosition;
        }
        else
        {
            var node = actor.GetNode(point.OffsetNodeType);
            position = node.TransformPoint(point.OffsetPos);
            rotation = node.rotation * rotation;
            scale = Vector3.Scale(node.lossyScale, scale);
        }
    }

    /// <summary>应用挂载、世界缩放以及重置旋转/缩放选项。</summary>
    public static void Apply(Transform effect, List<BattleUnitTimelineActor> actors,
        TimelineClipPointData point, Vector3 origin)
    {
        Evaluate(actors, point, origin, out var position, out var rotation, out var scale);
        var actor = actors[0];
        var parent = point.NeedSetParent && point.OffsetNodeType != UnitNodeType.World
            && point.OffsetNodeType != UnitNodeType.GroupCenter && point.OffsetNodeType != UnitNodeType.UnitOriginPos
            ? actor.GetNode(point.OffsetNodeType) : null;
        effect.SetParent(parent, true);
        if (point.UseOriginalRotation)
        {
            var facing = point.OverlayCharacterFacing ? actor.Root.eulerAngles.y : 0f;
            rotation = Quaternion.Euler(0f, facing, 0f) * Quaternion.Euler(point.OffsetEuler);
        }
        effect.SetPositionAndRotation(position, rotation);
        if (point.UseOriginalScale)
        {
            scale = point.OffsetScale;
        }
        var parentScale = parent ? parent.lossyScale : Vector3.one;
        effect.localScale = new Vector3(Divide(scale.x, parentScale.x), Divide(scale.y, parentScale.y), Divide(scale.z, parentScale.z));
    }

    /// <summary>按目标挂点放置受击特效；3D 单位朝向已由逻辑根旋转表达。</summary>
    public static void ApplyHit(Transform effect, BattleUnitTimelineActor target,
        TimelineClipPointData point, BattleUnitTimelineActor source)
    {
        Apply(effect, new List<BattleUnitTimelineActor> { target }, point, source.Root.position);
    }

    private static float Divide(float value, float scale)
    {
        return Mathf.Abs(scale) > 0.000001f ? value / scale : value;
    }
}
