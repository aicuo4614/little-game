using System;
using UnityEngine;

/// <summary>相对节点的位置 / 旋转 / 缩放偏移。</summary>
[Serializable]
public class TimelineClipPointData
{
    /// <summary>参考节点。</summary>
    public UnitNodeType OffsetNodeType;
    /// <summary>相对位置。</summary>
    public Vector3 OffsetPos;
    /// <summary>相对欧拉角。</summary>
    public Vector3 OffsetEuler;
    /// <summary>相对大小。</summary>
    public Vector3 OffsetScale;
    /// <summary>若参考为节点，是否挂到该节点下。</summary>
    public bool NeedSetParent;
    /// <summary>
    /// 未挂载时：勾选后仅首帧写入世界位姿，之后不随参考点移动（落点定住）。
    /// 勾选挂载时无效。
    /// </summary>
    public bool DontFollow;
    /// <summary>
    /// 勾选「重置旋转」：忽略 prefab 原旋转，从 (0,0,0) 起算再叠相对欧拉（及可选角色朝向）。
    /// JSON 字段名沿用 <c>UseOriginalRotation</c>。与挂载同时开启时每帧按重置结果写回（不跟骨骼转）。
    /// </summary>
    public bool UseOriginalRotation;
    /// <summary>
    /// 仅 <see cref="UseOriginalRotation"/> 勾选时生效。是否把作用角色的水平朝向（Y）叠进特效旋转；默认勾选。
    /// 最终：(0,0,0) * 角色Y朝向 * 相对欧拉。
    /// </summary>
    public bool OverlayCharacterFacing = true;
    /// <summary>
    /// 勾选「重置缩放」：忽略 prefab 原缩放，从 (1,1,1) 起算再乘 <see cref="OffsetScale"/>。
    /// JSON 字段名沿用 <c>UseOriginalScale</c>。与挂载同时开启时每帧写回（不跟骨骼缩放）。
    /// </summary>
    public bool UseOriginalScale;
}
