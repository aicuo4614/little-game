using System;
using UnityEngine;

/// <summary>从位移进入时的起点和选定参考点建立坐标系，供实战、预览及场景手柄共同使用。</summary>
public static class TimelineMoveOffsetUtil
{
    /// <summary>在参考点上应用偏移。接近方向模式忽略模型缩放，距离单位为世界米。</summary>
    public static Vector3 ToWorld(Vector3 start, Vector3 anchor, Vector3 offset,
        TimelineMoveOffsetSpace space, Quaternion nodeRotation, Vector3 nodeScale, Vector3 fallbackDirection)
    {
        if (space == TimelineMoveOffsetSpace.World)
        {
            return anchor + offset;
        }
        if (space == TimelineMoveOffsetSpace.NodeLocal)
        {
            return anchor + nodeRotation * Vector3.Scale(offset, nodeScale);
        }
        if (space != TimelineMoveOffsetSpace.ApproachDirection)
        {
            throw new ArgumentOutOfRangeException(nameof(space));
        }
        Basis(start, anchor, fallbackDirection, out var forward, out var side);
        return anchor + forward * offset.x + Vector3.up * offset.y + side * offset.z;
    }

    /// <summary>把场景手柄的世界位置转换回配置偏移；与 ToWorld 使用完全相同的轴。</summary>
    public static Vector3 FromWorld(Vector3 start, Vector3 anchor, Vector3 position,
        TimelineMoveOffsetSpace space, Quaternion nodeRotation, Vector3 nodeScale, Vector3 fallbackDirection)
    {
        var delta = position - anchor;
        if (space == TimelineMoveOffsetSpace.World)
        {
            return delta;
        }
        if (space == TimelineMoveOffsetSpace.NodeLocal)
        {
            var local = Quaternion.Inverse(nodeRotation) * delta;
            return new Vector3(Divide(local.x, nodeScale.x), Divide(local.y, nodeScale.y), Divide(local.z, nodeScale.z));
        }
        if (space != TimelineMoveOffsetSpace.ApproachDirection)
        {
            throw new ArgumentOutOfRangeException(nameof(space));
        }
        Basis(start, anchor, fallbackDirection, out var forward, out var side);
        return new Vector3(Vector3.Dot(delta, forward), delta.y, Vector3.Dot(delta, side));
    }

    /// <summary>使用地面方向；重合时用进入时朝向，再退回固定世界 X，避免零向量和 NaN。</summary>
    public static void Basis(Vector3 start, Vector3 anchor, Vector3 fallbackDirection,
        out Vector3 forward, out Vector3 side)
    {
        forward = anchor - start;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.000001f)
        {
            forward = fallbackDirection;
            forward.y = 0f;
        }
        if (forward.sqrMagnitude <= 0.000001f)
        {
            forward = Vector3.right;
        }
        forward.Normalize();
        side = Vector3.Cross(forward, Vector3.up);
    }

    private static float Divide(float value, float scale)
    {
        return Mathf.Abs(scale) > 0.000001f ? value / scale : 0f;
    }
}
