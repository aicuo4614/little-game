using UnityEngine;

/// <summary>实战与技能预览共用的竖屏基础机位；细节放大只用于编辑器观察。</summary>
public static class PortraitBattleCameraSettings
{
    public static readonly Vector3 LookTarget = new Vector3(0.25f, 0f, -0.25f);
    public static readonly Vector3 Offset = new Vector3(0f, 14f, -17.35f);
    public const float OrthographicSize = 11.25f;
    public const float Aspect = 9f / 16f;
    public static Quaternion Rotation => Quaternion.LookRotation(-Offset, Vector3.up);
}
