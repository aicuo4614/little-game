/// <summary>弹道视觉朝向；旧配置缺字段时保持三维轨迹朝向。</summary>
public enum TimelineBulletFacingType
{
    /// <summary>沿三维轨迹旋转弹道根，保留 Prefab 的视觉旋转。</summary>
    Trajectory3D = 0,
    /// <summary>Spine 子节点面向相机，并沿画面中的飞行方向旋转。</summary>
    CameraBillboard = 1
}
