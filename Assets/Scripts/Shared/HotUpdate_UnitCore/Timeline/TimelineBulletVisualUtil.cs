using UnityEngine;

/// <summary>纯播放计算，不挂载到 Prefab；由共享播放器在运镜采样后对齐二维弹道。</summary>
public static class TimelineBulletVisualUtil
{
    /// <summary>保持视觉平面朝向相机，按投影方向旋转；方向退化时使用固定角度，支持重复 Seek。</summary>
    public static void Align(Transform visual, Vector3 position, Camera camera, Vector3 direction, float angleOffset)
    {
        var from = camera.WorldToScreenPoint(position);
        var to = camera.WorldToScreenPoint(position + direction);
        var projected = new Vector2(to.x - from.x, to.y - from.y);
        var angle = from.z > 0f && to.z > 0f && projected.sqrMagnitude > 0.000001f
            ? Mathf.Atan2(projected.y, projected.x) * Mathf.Rad2Deg : 0f;
        // 世界旋转抵消逻辑根的三维旋转；负 X 缩放补偿素材前进轴。
        if (visual.lossyScale.x < 0f)
        {
            angle += 180f;
        }
        visual.rotation = camera.transform.rotation * Quaternion.AngleAxis(angle + angleOffset, Vector3.forward);
    }
}
