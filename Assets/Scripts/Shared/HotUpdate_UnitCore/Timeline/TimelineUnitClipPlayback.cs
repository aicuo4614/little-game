using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>位移、旋转、缩放和朝向目标的共同采样器，编辑器与运行端只提供单位解析。</summary>
public sealed class TimelineUnitClipPlayback
{
    private TimelineClipUnitConfigBase config;
    private Func<TimelineUnitSubjectType, string, List<BattleUnitTimelineActor>> resolve;
    private List<TimelineUnitClipState> states = new List<TimelineUnitClipState>();
    private bool captured;
    /// <summary>当前片段快照，供编辑器绘制终点提示。</summary>
    public IReadOnlyList<TimelineUnitClipState> States => states;

    /// <summary>绑定片段及其对象解析器。对象只在进入时选定。</summary>
    public TimelineUnitClipPlayback(TimelineClipUnitConfigBase config,
        Func<TimelineUnitSubjectType, string, List<BattleUnitTimelineActor>> resolve)
    {
        this.config = config;
        this.resolve = resolve;
    }

    /// <summary>捕获每个作用单位的起点和独立终点，不改变场景。</summary>
    public void Capture()
    {
        states.Clear();
        captured = true;
        var units = resolve(config.Subject, config.SubjectBindingKey);
        for (var i = 0; i < units.Count; i++)
        {
            var actor = units[i];
            var state = new TimelineUnitClipState
            {
                Actor = actor, Position = actor.Root.position,
                Rotation = actor.Visual.localRotation, Scale = actor.Visual.localScale,
                RootRotation = actor.Root.rotation, Facing = actor.Facing,
                OffsetDirection = actor.MovementFacing
            };
            if (config is TimelineClipTranslateConfig move)
            {
                if (!TryEndpoint(state, move.Destination, move.DestinationBindingKey,
                    move.ReferenceSelection, move.PointData, out state.End))
                {
                    continue;
                }
            }
            else if (config is TimelineClipRoleRotateToTargetConfig face)
            {
                if (!TryEndpoint(state, face.Destination, face.DestinationBindingKey,
                    face.ReferenceSelection, null, out state.End))
                {
                    continue;
                }
            }
            states.Add(state);
        }
    }

    /// <summary>求值片段时间，duration 为调度后的实际时长。完成后保留终态供下一片段接入。</summary>
    public void Sample(float time, float duration, Func<BattleUnitTimelineActor, float> unitTime = null)
    {
        if (!captured)
        {
            Capture();
        }
        for (var i = 0; i < states.Count; i++)
        {
            var state = states[i];
            var timing = unitTime is null ? time : unitTime(state.Actor);
            var progress = duration > 0f ? Mathf.Clamp01(timing / duration) : 1f;
            switch (config)
            {
                case TimelineClipTranslateConfig move:
                {
                    state.Actor.Root.position = Vector3.Lerp(state.Position, state.End, progress);
                    if (move.IsRotateToAtStart)
                    {
                        state.Actor.Face(state.End - state.Position);
                    }
                    break;
                }
                case TimelineClipRotateConfig rotate:
                {
                    state.Actor.Visual.localRotation = state.Rotation * Quaternion.Euler(
                        Vector3.Lerp(rotate.StartEuler, rotate.EndEuler, progress));
                    break;
                }
                case TimelineClipScaleConfig scale:
                {
                    state.Actor.Visual.localScale = Vector3.Scale(state.Scale,
                        Vector3.Lerp(scale.StartScale, scale.EndScale, progress));
                    break;
                }
                case TimelineClipRoleRotateToTargetConfig:
                {
                    state.Actor.Face(state.End - state.Position);
                    break;
                }
            }
        }
    }

    /// <summary>按当前对象计算编辑器自动时长；速度模式在导出时写入 JSON。</summary>
    public float MeasureDuration()
    {
        switch (config)
        {
            case TimelineClipRotateConfig rotate:
            {
                return Duration(rotate.DurationType, rotate.DurationPraram,
                    Vector3.Distance(rotate.StartEuler, rotate.EndEuler));
            }
            case TimelineClipScaleConfig scale:
            {
                return Duration(scale.DurationType, scale.DurationPraram,
                    Vector3.Distance(scale.StartScale, scale.EndScale));
            }
            case TimelineClipTranslateConfig move:
            {
                if (move.DurationType == DurationType.Time)
                {
                    return Mathf.Max(0.01f, move.DurationPraram);
                }
                var wasCaptured = captured;
                if (!wasCaptured)
                {
                    Capture();
                }
                var distance = 0f;
                for (var i = 0; i < states.Count; i++)
                {
                    distance = Mathf.Max(distance, Vector3.Distance(states[i].Position, states[i].End));
                }
                if (!wasCaptured)
                {
                    Clear();
                }
                return Duration(move.DurationType, move.DurationPraram, distance);
            }
            default:
            {
                return 0f;
            }
        }
    }

    /// <summary>清除进入快照；整体恢复由宿主的单位适配层统一完成。</summary>
    public void Clear()
    {
        states.Clear();
        captured = false;
    }

    /// <summary>反向离开片段时只还原本片段拥有的变换通道。</summary>
    public void RestoreEntry()
    {
        for (var i = 0; i < states.Count; i++)
        {
            var state = states[i];
            switch (config)
            {
                case TimelineClipTranslateConfig move:
                {
                    state.Actor.Root.position = state.Position;
                    if (move.IsRotateToAtStart)
                    {
                        state.Actor.RestoreFacing(state.RootRotation, state.Facing);
                    }
                    break;
                }
                case TimelineClipRotateConfig:
                {
                    state.Actor.Visual.localRotation = state.Rotation;
                    break;
                }
                case TimelineClipScaleConfig:
                {
                    state.Actor.Visual.localScale = state.Scale;
                    break;
                }
                case TimelineClipRoleRotateToTargetConfig:
                {
                    state.Actor.RestoreFacing(state.RootRotation, state.Facing);
                    break;
                }
            }
        }
    }

    /// <summary>校验单位片段的角色、参考方式、时长参数和空间数据。</summary>
    public static void Validate(TimelineClipUnitConfigBase config)
    {
        if (!Enum.IsDefined(typeof(TimelineUnitSubjectType), config.Subject))
        {
            throw new ArgumentException("Unknown unit subject: " + config.ClipKey);
        }
        switch (config)
        {
            case TimelineClipRotateConfig rotate:
            {
                CheckDuration(rotate.DurationType, rotate.DurationPraram);
                CheckVector(rotate.StartEuler);
                CheckVector(rotate.EndEuler);
                break;
            }
            case TimelineClipScaleConfig scale:
            {
                CheckDuration(scale.DurationType, scale.DurationPraram);
                CheckVector(scale.StartScale);
                CheckVector(scale.EndScale);
                break;
            }
            case TimelineClipTranslateConfig move:
            {
                CheckDuration(move.DurationType, move.DurationPraram);
                if (!Enum.IsDefined(typeof(TimelineMoveOffsetSpace), move.OffsetSpace))
                {
                    throw new ArgumentException("Unknown movement offset space: " + move.ClipKey);
                }
                CheckReference(move.Destination, move.ReferenceSelection, true);
                if (move.PointData is not TimelineClipPointData point)
                {
                    throw new ArgumentException("Movement point data is missing.");
                }
                CheckVector(point.OffsetPos);
                if (move.Destination <= TimelineMoveDestinationType.EffectTargets
                    && point.OffsetNodeType == UnitNodeType.World)
                {
                    throw new ArgumentException("Use WorldPosition destination for a world point, or select a unit attachment node.");
                }
                break;
            }
            case TimelineClipRoleRotateToTargetConfig face:
            {
                CheckReference(face.Destination, face.ReferenceSelection, false);
                break;
            }
        }
    }

    private static void CheckReference(TimelineMoveDestinationType destination, TimelineMoveReferenceType selection, bool positions)
    {
        if (!Enum.IsDefined(typeof(TimelineMoveDestinationType), destination)
            || !Enum.IsDefined(typeof(TimelineMoveReferenceType), selection)
            || (!positions && destination > TimelineMoveDestinationType.EffectTargets))
        {
            throw new ArgumentException("Unknown unit reference or unsupported facing destination.");
        }
    }

    private static void CheckDuration(DurationType type, float value)
    {
        if ((type != DurationType.Time && type != DurationType.Speed)
            || !(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentException("Unit duration/speed must be finite and positive.");
        }
    }

    private static void CheckVector(Vector3 value)
    {
        if (float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z)
            || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z))
        {
            throw new ArgumentException("Unit transform values must be finite.");
        }
    }

    private bool TryEndpoint(TimelineUnitClipState state, TimelineMoveDestinationType destination,
        string key, TimelineMoveReferenceType selection, TimelineClipPointData point, out Vector3 end)
    {
        var offset = point is TimelineClipPointData ? point.OffsetPos : Vector3.zero;
        var space = config is TimelineClipTranslateConfig move ? move.OffsetSpace : TimelineMoveOffsetSpace.World;
        if (destination == TimelineMoveDestinationType.WorldPosition)
        {
            end = offset;
            return true;
        }
        if (destination == TimelineMoveDestinationType.InitialPosition)
        {
            end = ApplyOffset(state, state.Actor.InitialPosition, state.Actor.Root.rotation, state.Actor.Root.lossyScale, offset, space);
            return true;
        }
        var subject = destination == TimelineMoveDestinationType.CastTargets ? TimelineUnitSubjectType.CastTargets
            : destination == TimelineMoveDestinationType.EffectSource ? TimelineUnitSubjectType.EffectSource : TimelineUnitSubjectType.EffectTargets;
        var references = resolve(subject, key);
        if (references.Count == 0)
        {
            end = state.Position;
            return false;
        }
        if (selection == TimelineMoveReferenceType.Center)
        {
            var sum = Vector3.zero;
            foreach (var reference in references)
            {
                sum += ReferenceAnchor(reference, point);
            }
            var firstNode = references[0].GetNode(point is TimelineClipPointData ? point.OffsetNodeType : UnitNodeType.Transform);
            end = ApplyOffset(state, sum / references.Count, firstNode.rotation, firstNode.lossyScale, offset, space);
            return true;
        }

        var selected = references[0];
        var distance = (selected.Root.position - state.Position).sqrMagnitude;
        var farthest = selection == TimelineMoveReferenceType.Farthest;
        for (var i = 1; i < references.Count; i++)
        {
            var reference = references[i];
            var candidate = (reference.Root.position - state.Position).sqrMagnitude;
            if (farthest ? candidate > distance : candidate < distance)
            {
                selected = reference;
                distance = candidate;
            }
        }
        var selectedNode = selected.GetNode(point is TimelineClipPointData ? point.OffsetNodeType : UnitNodeType.Transform);
        end = ApplyOffset(state, ReferenceAnchor(selected, point), selectedNode.rotation, selectedNode.lossyScale, offset, space);
        return true;
    }

    private static Vector3 ReferenceAnchor(BattleUnitTimelineActor actor, TimelineClipPointData point)
    {
        if (point is not TimelineClipPointData)
        {
            return actor.Root.position;
        }
        if (point.OffsetNodeType == UnitNodeType.UnitOriginPos)
        {
            return actor.InitialPosition;
        }
        if (point.OffsetNodeType == UnitNodeType.GroupCenter)
        {
            return actor.Root.position;
        }
        return actor.GetNode(point.OffsetNodeType).position;
    }

    private static Vector3 ApplyOffset(TimelineUnitClipState state, Vector3 anchor, Quaternion rotation,
        Vector3 scale, Vector3 offset, TimelineMoveOffsetSpace space)
    {
        state.Anchor = anchor;
        state.AnchorRotation = rotation;
        state.AnchorScale = scale;
        return TimelineMoveOffsetUtil.ToWorld(state.Position, anchor, offset, space, rotation, scale, state.OffsetDirection);
    }

    private static float Duration(DurationType type, float parameter, float distance)
    {
        return type == DurationType.Time ? Mathf.Max(0.01f, parameter)
            : Mathf.Max(0.01f, distance / Mathf.Max(0.01f, parameter));
    }
}
