using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 3D 单位在技能演出中的适配数据（对齐 Demo BattleUnitTimelineActor，原生基于 3D Animator 与 UnitMono 挂点）。
/// 逻辑根负责位置与朝向，表现节点负责局部旋转与缩放。
/// </summary>
public sealed class BattleUnitTimelineActor
{
    public Transform Root { get; }
    public Transform Visual { get; }
    public Vector3 InitialPosition { get; }
    /// <summary>3D 单位动画组件；占位形象可为空。</summary>
    public Animator Animator => animator;
    public UnitTeamType Team { get; set; }
    public float Facing => 1f;
    public Vector3 MovementFacing => Root ? Root.forward : Vector3.forward;

    private readonly Animator animator;
    private readonly Renderer bodyRenderer;
    private readonly Quaternion initialRootRotation;
    private readonly Quaternion initialRotation;
    private readonly Vector3 initialScale;
    private readonly Func<Vector3> cameraRight;
    private readonly Func<UnitNodeType, Transform> findNode;
    private readonly Dictionary<UnitNodeType, Transform> nodes = new Dictionary<UnitNodeType, Transform>();
    private readonly HashSet<UnitNodeType> warnedCameraNodes = new HashSet<UnitNodeType>();

    public BattleUnitTimelineActor(
        Transform root,
        Transform visual,
        Animator animator,
        Func<Vector3> cameraRight,
        Func<UnitNodeType, Transform> findNode,
        Vector3? initialPosition = null)
    {
        Root = root;
        Visual = visual != null ? visual : root;
        this.animator = animator;
        bodyRenderer = Visual ? Visual.GetComponentInChildren<Renderer>() : null;
        this.cameraRight = cameraRight;
        this.findNode = findNode;
        InitialPosition = initialPosition ?? (root ? root.position : Vector3.zero);
        initialRootRotation = root ? root.rotation : Quaternion.identity;
        initialRotation = Visual ? Visual.localRotation : Quaternion.identity;
        initialScale = Visual ? Visual.localScale : Vector3.one;
    }

    public Vector3 GetPoint(TimelineClipPointData point, Vector3 origin)
    {
        if (point == null)
        {
            return Root ? Root.position : Vector3.zero;
        }

        if (point.OffsetNodeType == UnitNodeType.World)
        {
            return point.OffsetPos;
        }

        if (point.OffsetNodeType == UnitNodeType.UnitOriginPos)
        {
            return InitialPosition;
        }

        if (point.OffsetNodeType == UnitNodeType.GroupCenter)
        {
            var direction = origin - Root.position;
            return Root.position + (direction.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(direction) : Quaternion.identity) * point.OffsetPos;
        }

        return GetNode(point.OffsetNodeType).TransformPoint(point.OffsetPos);
    }

    public Transform GetNode(UnitNodeType type)
    {
        var node = FindNodeCached(type);
        return node ? node : Root;
    }

    public bool HasCameraNode(UnitNodeType type)
    {
        return type is UnitNodeType.None or UnitNodeType.Transform or UnitNodeType.Root
            or UnitNodeType.UnitOriginPos or UnitNodeType.BodyCenter
            || FindNodeCached(type);
    }

    public Vector3 GetCameraPoint(UnitNodeType type)
    {
        if (type == UnitNodeType.UnitOriginPos)
        {
            return InitialPosition;
        }

        if (type is UnitNodeType.None or UnitNodeType.Transform)
        {
            return Root.position;
        }

        var node = FindNodeCached(type);
        if (node)
        {
            return node.position;
        }

        if (type == UnitNodeType.Root)
        {
            return Root.position;
        }

        if (type != UnitNodeType.BodyCenter && warnedCameraNodes.Add(type))
        {
            Debug.LogWarning($"Camera node missing: unit={Root.name}, node={type}, fallback=BodyCenter");
        }

        var center = FindNodeCached(UnitNodeType.BodyCenter);
        if (center)
        {
            return center.position;
        }

        return bodyRenderer ? bodyRenderer.bounds.center : Root.position + Vector3.up * 0.6f;
    }

    private Transform FindNodeCached(UnitNodeType type)
    {
        if (!nodes.TryGetValue(type, out var node))
        {
            node = findNode != null ? findNode(type) : null;
            nodes.Add(type, node);
        }

        return node;
    }

    public void Face(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.000001f || !Root)
        {
            return;
        }

        Root.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    public void Restore()
    {
        if (Root)
        {
            Root.position = InitialPosition;
            Root.rotation = initialRootRotation;
        }

        if (Visual)
        {
            Visual.localRotation = initialRotation;
            Visual.localScale = initialScale;
        }
    }

    public void RestoreFacing(Quaternion rootRotation, float facing)
    {
        if (Root)
        {
            Root.rotation = rootRotation;
        }
    }
}
