using UnityEngine;

/// <summary>Timeline 共用纯数学（不依赖 UnitMono / ResUtil）。</summary>
public static class TimelineMathUtil
{
    /// <summary>相对群体中心，按主角朝向施加偏移后的世界坐标。</summary>
    public static Vector3 GetTeamCenterByOffset(Vector3 teamCenter, Vector3 originPos, Vector3 offset)
    {
        var direction = originPos - teamCenter;
        var rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        return teamCenter + rotation * offset;
    }

    /// <summary>绕弦中心旋转后的点和方向。</summary>
    public static void GetPointOnArcFromChord(
        Vector3 a,
        Vector3 b,
        float r,
        float yOffset,
        float zOffset,
        out Vector3 point,
        out Vector3 direction)
    {
        Vector3 v = b - a;
        Vector3 center = (b + a) / 2;
        float radius = (center - a).magnitude;
        Quaternion rotation = Quaternion.AngleAxis(r, Vector3.up);
        Vector3 newDirection = rotation * v;
        Vector3 newDirectionNormalized = newDirection.normalized;
        point = center + (-newDirectionNormalized) * radius;
        point.y += yOffset;
        point = point + newDirectionNormalized * zOffset;
        direction = newDirectionNormalized;
    }
}
