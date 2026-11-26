using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 按 UnitMono 节点解析世界变换（编辑器预览与运行时共用，无 ResUtil / Unit 依赖）。
/// </summary>
public static class TimelineTransformUtil
{
    private static List<UnitMono> unitsCache = new List<UnitMono>();

    public static void GetTransformationFromUnitNodePointData(
        Vector3 originPos,
        UnitMono unit,
        TimelineClipPointData pointData,
        out Vector3 position,
        out Quaternion quaternion,
        out Vector3 scale)
    {
        unitsCache.Clear();
        unitsCache.Add(unit);
        GetTransformationFromUnitNodePointData(originPos, unitsCache, pointData, out position, out quaternion, out scale);
    }

    public static void GetTransformationFromUnitNodePointData(
        Vector3 originPos,
        List<UnitMono> units,
        TimelineClipPointData pointData,
        out Vector3 position,
        out Quaternion quaternion,
        out Vector3 scale)
    {
        if (pointData.OffsetNodeType == UnitNodeType.World)
        {
            position = pointData.OffsetPos;
            quaternion = Quaternion.Euler(pointData.OffsetEuler);
            scale = pointData.OffsetScale;
        }
        else if (pointData.OffsetNodeType == UnitNodeType.GroupCenter)
        {
            var teamCenter = GetTeamCenter(units);
            position = TimelineMathUtil.GetTeamCenterByOffset(teamCenter, originPos, pointData.OffsetPos);
            quaternion = Quaternion.LookRotation(teamCenter - position) * Quaternion.Euler(pointData.OffsetEuler);
            scale = pointData.OffsetScale;
        }
        else if (pointData.OffsetNodeType == UnitNodeType.UnitOriginPos)
        {
            var target = units[0];
            position = target.BattleOriginPos;
            quaternion = Quaternion.Euler(pointData.OffsetEuler);
            scale = pointData.OffsetScale;
        }
        else
        {
            if (units.Count == 0)
            {
                position = pointData.OffsetPos;
                quaternion = Quaternion.Euler(pointData.OffsetEuler);
                scale = pointData.OffsetScale;
                return;
            }

            var target = units[0];
            // 节点未绑定（编辑器未 Apply JSON / 骨骼名猜失败）时回退到单位根，避免 TransformPoint NRE
            var offsetNodeTrans = target.FindNodeTransform(pointData.OffsetNodeType);
            if (offsetNodeTrans == null)
            {
                offsetNodeTrans = target.Transform;
            }

            position = offsetNodeTrans.TransformPoint(pointData.OffsetPos);
            quaternion = offsetNodeTrans.rotation * Quaternion.Euler(pointData.OffsetEuler);
            scale = Vector3.Scale(offsetNodeTrans.lossyScale, pointData.OffsetScale);
        }
    }

    public static Transform GetParentFromUnitNodePointData(TimelineClipPointData pointData, UnitMono target)
    {
        if (pointData == null
            || !pointData.NeedSetParent
            || pointData.OffsetNodeType == UnitNodeType.World
            || pointData.OffsetNodeType == UnitNodeType.GroupCenter
            || pointData.OffsetNodeType == UnitNodeType.UnitOriginPos)
        {
            return null;
        }

        var node = target.FindNodeTransform(pointData.OffsetNodeType);
        return node != null ? node : target.Transform;
    }

    /// <summary>
    /// 将查询得到的世界变换应用到特效 Transform。
    /// 勾选重置旋转：从 (0,0,0) 起算 * 作用角色Y朝向 * OffsetEuler；
    /// 勾选重置缩放：从 (1,1,1) 起算再乘 OffsetScale。
    /// 未勾选则仍用查询点算出的世界旋转/缩放。与挂载同时开启时，调用方需每帧调用本方法。
    /// </summary>
    /// <param name="facingUnit">叠加角色朝向时取该单位水平朝向（挂点所属角色）。</param>
    public static void ApplyEffectTransform(
        Transform effectTransform,
        TimelineClipPointData pointData,
        Transform parent,
        Vector3 position,
        Quaternion rotation,
        Vector3 worldScale,
        UnitMono facingUnit)
    {
        effectTransform.SetParent(parent);

        // 重置旋转：Identity *（可选）作用角色 Y 朝向 * 相对欧拉；否则用查询点算出的世界旋转（已含 OffsetEuler）
        Quaternion finalRotation;
        if (pointData.UseOriginalRotation)
        {
            finalRotation = Quaternion.Euler(ResolveOriginalRotationFacingOverlayEuler(pointData, facingUnit))
                * Quaternion.Euler(pointData.OffsetEuler);
        }
        else
        {
            finalRotation = rotation;
        }

        effectTransform.SetPositionAndRotation(position, finalRotation);

        if (pointData.UseOriginalScale)
        {
            // 重置缩放：在 (1,1,1) 上乘相对缩放
            effectTransform.localScale = pointData.OffsetScale;
            return;
        }

        var parentLossy = effectTransform.parent != null
            ? effectTransform.parent.lossyScale
            : Vector3.one;
        effectTransform.localScale = new Vector3(
            worldScale.x / parentLossy.x,
            worldScale.y / parentLossy.y,
            worldScale.z / parentLossy.z);
    }

    /// <summary>
    /// 「重置旋转」且勾选叠加角色朝向时：叠作用角色当前水平朝向（Transform.eulerAngles.y）。
    /// 例：角色 (0,90,0)、相对 (0,180,0) → (0,270,0)。
    /// </summary>
    public static Vector3 ResolveOriginalRotationFacingOverlayEuler(
        TimelineClipPointData pointData,
        UnitMono facingUnit)
    {
        if (!pointData.UseOriginalRotation || !pointData.OverlayCharacterFacing)
        {
            return Vector3.zero;
        }

        if (facingUnit == null)
        {
            return Vector3.zero;
        }

        return new Vector3(0f, facingUnit.Transform.eulerAngles.y, 0f);
    }

    public static Vector3 GetTeamCenter(IReadOnlyList<UnitMono> units)
    {
        if (units == null || units.Count == 0)
        {
            return Vector3.zero;
        }

        var center = Vector3.zero;
        for (var i = 0; i < units.Count; i++)
        {
            center += units[i].transform.position;
        }

        return center / units.Count;
    }

    /// <summary>技能相机默认按 TeamA（左、朝右）编辑；TeamB 需镜像时返回 true。</summary>
    public static bool NeedsTeamCameraMirror(bool mirrorByTeam, UnitTeamType team)
    {
        return mirrorByTeam && team == UnitTeamType.TeamB;
    }

    /// <summary>绕战场中线（X=0）镜像世界坐标。</summary>
    public static Vector3 MirrorBattleX(Vector3 worldPos)
    {
        return new Vector3(-worldPos.x, worldPos.y, worldPos.z);
    }

    /// <summary>
    /// 绕战场中线（X=0）镜像世界旋转：yaw 取反，pitch/roll 保持。
    /// 用于换边同款构图；不做几何反射（那会把 roll 一起翻号，荷兰角左右颠倒）。
    /// </summary>
    public static Quaternion MirrorBattleRotX(Quaternion worldRot)
    {
        var euler = worldRot.eulerAngles;
        var yaw = euler.y;
        if (yaw > 180f)
        {
            yaw -= 360f;
        }

        return Quaternion.Euler(euler.x, -yaw, euler.z);
    }

    /// <summary>
    /// 相机轨迹采样后、挂在施法者节点下的本地镜像：localPosition.x 取反，localRotation 走 keep-roll。
    /// 在 SampleAnimation / Animation.Play 写完 Transform 之后调用（编辑器采样后或运行时 LateUpdate）。
    /// </summary>
    public static void MirrorBattleLocalTransformX(Transform transform)
    {
        var localPos = transform.localPosition;
        transform.localPosition = new Vector3(-localPos.x, localPos.y, localPos.z);
        transform.localRotation = MirrorBattleRotX(transform.localRotation);
    }

    /// <summary>世界坐标 → 编辑空间（TeamA 侧）；不需要镜像时原样返回。</summary>
    public static Vector3 ToAuthoringPos(Vector3 worldPos, bool mirror)
    {
        return mirror ? MirrorBattleX(worldPos) : worldPos;
    }

    /// <summary>世界旋转 → 编辑空间；不需要镜像时原样返回。</summary>
    public static Quaternion ToAuthoringRot(Quaternion worldRot, bool mirror)
    {
        return mirror ? MirrorBattleRotX(worldRot) : worldRot;
    }

    /// <summary>编辑空间结果 → 世界（TeamB 时再镜像回去）。</summary>
    public static Vector3 FromAuthoringPos(Vector3 authoringPos, bool mirror)
    {
        return mirror ? MirrorBattleX(authoringPos) : authoringPos;
    }

    /// <summary>编辑空间旋转 → 世界。</summary>
    public static Quaternion FromAuthoringRot(Quaternion authoringRot, bool mirror)
    {
        return mirror ? MirrorBattleRotX(authoringRot) : authoringRot;
    }
}
