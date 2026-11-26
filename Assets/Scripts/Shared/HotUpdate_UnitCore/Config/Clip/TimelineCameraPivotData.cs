using System;
using UnityEngine;

/// <summary>运镜的一端注视参考。对象在进入时选定，节点坐标随角色更新，偏移按战场世界轴镜像。</summary>
[Serializable]
public sealed class TimelineCameraPivotData
{
    /// <summary>注视角色：施法者、施法对象、效果来源或效果对象。</summary>
    public TimelineUnitSubjectType Subject;
    /// <summary>独立对象绑定；空值取片段开始前最近的同类绑定。</summary>
    public string BindingKey;
    /// <summary>多对象取点；最近/最远在进入时选定，全体中心保持相同对象集合。</summary>
    public TimelineMoveReferenceType Selection = TimelineMoveReferenceType.Center;
    /// <summary>身体中心为 2D/3D 通用构图点；单位原点表示演出开始前的位置。</summary>
    public UnitNodeType NodeType = UnitNodeType.BodyCenter;
    /// <summary>勾选后取施法者与所选对象参考点的中点，双方使用各自的同名节点。</summary>
    public bool IncludeCaster;
    /// <summary>世界轴偏移，单位米；阵营镜像只翻转偏移 X。</summary>
    public Vector3 Offset;
    /// <summary>锁定此端进入时的世界注视点；关闭时随绑定单位移动。</summary>
    public bool LockAtEntry;
}
