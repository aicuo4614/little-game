using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单位场景表现壳（节点 / 阵营 / 位移），编辑器预览与运行时共用。
/// 逻辑宿主 <see cref="LogicUnit"/> 可为空（编辑器未 Init）；运行时由 HotUpdate_Main 绑定为 <c>Unit</c>。
/// </summary>
public class UnitMono : MonoBehaviour
{
    public Animator Animator;

    /// <summary>
    /// 位移由 Animator Root Motion 驱动（平地走、攀墙）。
    /// 为 false 时由 NavMeshAgent / 碰撞推移。攀墙 IK 目前也看这个开关。
    /// 运行时由出生参数写入，不从 Avatar 或手骨推断。
    /// </summary>
    public bool UseRootMotion;

    public Transform ActiveRoot;

    /// <summary>动画事件宿主（运行时多为 UnitAnimationEventMono）；编辑器可为空。</summary>
    public IUnitAnimationEventHost AnimationEvents;

    public List<UnitNodeData> UnitNodeDatas;

    public UnitTeamType TeamType;

    private Dictionary<UnitNodeType, UnitNodeData> dict = new Dictionary<UnitNodeType, UnitNodeData>();

    public Vector3 BattleOriginPos { get; set; }
    public Quaternion BattleOriginRotation;

    private Vector3 curAttackForce;
    public Vector3 CurAttackForce => curAttackForce;

    /// <summary>逻辑宿主对象，运行时为 Unit；编辑器预览可为空。</summary>
    public object LogicUnit { get; private set; }

    private Action onDrawGizmos;

    /// <summary>绑定逻辑宿主（不引用 HotUpdate_Main 的 Unit 类型，避免程序集环依赖）。</summary>
    public void BindLogicUnit(object logicUnit, Action drawGizmos = null)
    {
        LogicUnit = logicUnit;
        onDrawGizmos = drawGizmos;
    }

    public void InitTeamType(UnitTeamType teamType)
    {
        TeamType = teamType;
    }

    public void SetAttackForce(Vector3 force)
    {
        curAttackForce = force;
    }

    public void ResetAttackForce()
    {
        curAttackForce = Vector3.zero;
    }

    public UnitNodeData FindNodeData(UnitNodeType nodeType)
    {
        if (dict.ContainsKey(nodeType))
        {
            return dict[nodeType];
        }

        if (UnitNodeDatas == null)
        {
            return null;
        }

        for (var i = 0; i < UnitNodeDatas.Count; i++)
        {
            if (UnitNodeDatas[i].UnitNodeType == nodeType)
            {
                dict.Add(nodeType, UnitNodeDatas[i]);
                return UnitNodeDatas[i];
            }
        }

        return null;
    }

    /// <summary>节点列表被 JSON / 重新绑定改写后清查找缓存。</summary>
    public void ClearNodeCache()
    {
        dict.Clear();
    }

    public Transform FindNodeTransform(UnitNodeType nodeType)
    {
        if (nodeType == UnitNodeType.Transform)
        {
            return transform;
        }

        var nodeData = FindNodeData(nodeType);
        if (nodeData == null)
        {
            return null;
        }

        return nodeData.UnitNodeTrans;
    }

    public Vector3 GetNodeWorldPos(UnitNodeType nodeType)
    {
        if (nodeType == UnitNodeType.Transform)
        {
            return transform.position;
        }

        var unitNodeData = FindNodeData(nodeType);
        if (unitNodeData == null || unitNodeData.UnitNodeTrans == null)
        {
            return transform.position;
        }

        return unitNodeData.UnitNodeTrans.position;
    }

    public Transform Transform => transform;

    public Vector3 GetTransformPosition()
    {
        return transform.position;
    }

    public void SetTransformPosition(Vector3 position)
    {
        transform.position = position;
    }

    private void OnDrawGizmos()
    {
        onDrawGizmos?.Invoke();
    }
}

/// <summary>动画事件宿主契约（实现在 HotUpdate_Main 的 UnitAnimationEventMono）。</summary>
public interface IUnitAnimationEventHost
{
    void OnClearEventFlags();
    void InitLogic(object logicUnit);
}

[Serializable]
public class UnitNodeData
{
    public UnitNodeType UnitNodeType;
    public Transform UnitNodeTrans;
}
