using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>相机注视端的对象快照。绑定和最近/最远只在进入时解析，位置在采样时更新。</summary>
public sealed class TimelineCameraPivotPlayback
{
    private TimelineCameraPivotData data;
    private List<BattleUnitTimelineActor> actors = new List<BattleUnitTimelineActor>();
    private BattleUnitTimelineActor caster;
    private bool mirror;
    private Vector3 entryPoint;

    /// <summary>捕获注视对象，不改变单位或相机。</summary>
    public TimelineCameraPivotPlayback(TimelineCameraPivotData data, TimelineClipConfigBase consumer,
        Func<TimelineClipConfigBase, TimelineUnitSubjectType, string, List<BattleUnitTimelineActor>> resolve,
        BattleUnitTimelineActor caster, bool mirror)
    {
        this.data = data;
        this.caster = caster;
        this.mirror = mirror;
        var candidates = resolve(consumer, data.Subject, data.BindingKey);
        if (candidates.Count == 0)
        {
            if (data.Subject != TimelineUnitSubjectType.Caster && string.IsNullOrEmpty(data.BindingKey))
            {
                // 历史空目标以施法者为参考，预览也允许尚未布置目标的画布。
                candidates = new List<BattleUnitTimelineActor> { caster };
            }
            else
            {
                throw new ArgumentException($"Camera pivot binding is empty: clip={consumer.ClipKey}, subject={data.Subject}, binding={data.BindingKey}");
            }
        }
        if (data.Selection == TimelineMoveReferenceType.Center)
        {
            actors.AddRange(candidates);
        }
        else
        {
            var selected = candidates[0];
            var distance = (selected.Root.position - caster.Root.position).sqrMagnitude;
            for (var i = 1; i < candidates.Count; i++)
            {
                var next = (candidates[i].Root.position - caster.Root.position).sqrMagnitude;
                if (data.Selection == TimelineMoveReferenceType.Nearest ? next < distance : next > distance)
                {
                    selected = candidates[i];
                    distance = next;
                }
            }
            actors.Add(selected);
        }
        if (data.LockAtEntry)
        {
            entryPoint = SamplePoint();
        }
    }

    /// <summary>所选对象的位置中心，用于捕获不受挂点和偏移影响的构图轴。</summary>
    public Vector3 ObjectCenter
    {
        get
        {
            var point = Vector3.zero;
            foreach (var actor in actors)
            {
                point += actor.Root.position;
            }
            return point / actors.Count;
        }
    }

    /// <summary>当前世界注视点，偏移只做一次阵营镜像。</summary>
    public Vector3 Point => data.LockAtEntry ? entryPoint : SamplePoint();

    private Vector3 SamplePoint()
    {
        var point = Vector3.zero;
        for (var i = 0; i < actors.Count; i++)
        {
            point += actors[i].GetCameraPoint(data.NodeType);
        }
        point /= actors.Count;
        if (data.IncludeCaster)
        {
            point = (point + caster.GetCameraPoint(data.NodeType)) * 0.5f;
        }
        return point + TimelineTransformUtil.FromAuthoringPos(data.Offset, mirror);
    }

    /// <summary>旧字段只读兼容：旧目标明确对应施法对象，旧中点保留施法者与对象中心的二等分。</summary>
    public static TimelineCameraPivotData Data(TimelineClipCameraShotConfig shot, bool from)
    {
        var configured = from ? shot.FromPivot : shot.LookPivot;
        if (configured is not null)
        {
            return configured;
        }
        var type = from ? shot.LookAtFromType : shot.LookAtType;
        var shoulder = from && shot.ShotType == TimelineCameraShotType.Ots;
        return new TimelineCameraPivotData
        {
            Subject = !shoulder && type is TimelineCameraShotLookAtType.Target or TimelineCameraShotLookAtType.SelfTargetCenter
                ? TimelineUnitSubjectType.CastTargets : TimelineUnitSubjectType.Caster,
            BindingKey = shot.TargetBindingKey,
            NodeType = from ? shot.LookAtFromNodeType : shot.LookAtNodeType,
            IncludeCaster = !shoulder && type == TimelineCameraShotLookAtType.SelfTargetCenter,
            Offset = shoulder ? Vector3.zero : shot.PivotOffset
        };
    }

    /// <summary>只有换枢轴、绝对起点甩镜和过肩消费起始端。</summary>
    public static bool UsesFrom(TimelineClipCameraShotConfig shot)
    {
        return shot.ShotType is TimelineCameraShotType.PivotBlend or TimelineCameraShotType.Ots
            || shot.ShotType == TimelineCameraShotType.WhipPan && !shot.FromCurrentPose;
    }

    /// <summary>约束相机节点为实际挂点或单位原位；世界和群组位置不伪装为骨骼。</summary>
    public static bool IsNode(UnitNodeType node)
    {
        return node is UnitNodeType.None or UnitNodeType.Transform or UnitNodeType.Root
            or UnitNodeType.Spine or UnitNodeType.Head or UnitNodeType.LeftHand or UnitNodeType.RightHand
            or UnitNodeType.Custom1 or UnitNodeType.Custom2 or UnitNodeType.Custom3
            or UnitNodeType.BodyCenter or UnitNodeType.UnitOriginPos;
    }

    /// <summary>校验新版注视字段；旧字段读取时保留历史数值。</summary>
    public static void Validate(TimelineCameraPivotData data)
    {
        if (!Enum.IsDefined(typeof(TimelineUnitSubjectType), data.Subject)
            || !Enum.IsDefined(typeof(TimelineMoveReferenceType), data.Selection) || !IsNode(data.NodeType)
            || !Finite(data.Offset.x) || !Finite(data.Offset.y) || !Finite(data.Offset.z))
        {
            throw new ArgumentException("Camera pivot subject, selection, node or offset is invalid.");
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
