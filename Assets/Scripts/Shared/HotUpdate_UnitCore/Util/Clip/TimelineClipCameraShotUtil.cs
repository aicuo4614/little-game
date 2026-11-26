using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 相机运镜模板共用算法（编辑器预览与运行时）：路径采样、LookAt、FOV。
/// </summary>
public static class TimelineClipCameraShotUtil
{
    private static readonly Dictionary<string, Vector3> holdFirstLookBase = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, Vector3> holdFirstEntryPos = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, Vector3> fromCurrentPushPivot = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, Vector3> fromCurrentPushDir = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, float> fromCurrentPushStartDist = new Dictionary<string, float>();
    private static readonly Dictionary<string, Vector3> craneTruckFirstStart = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, Vector3> craneTruckFirstRight = new Dictionary<string, Vector3>();

    /// <summary>编辑器离开 Clip / 停预览，或运行时 Stop 时清掉定格/推近/横移起重机等第一帧缓存。</summary>
    public static void ClearHoldFirstFramePose(string poseOwnerId, string clipKey)
    {
        var key = MakeHoldPoseKey(poseOwnerId, clipKey);
        if (key == null)
        {
            return;
        }

        holdFirstLookBase.Remove(key);
        holdFirstEntryPos.Remove(key);
        fromCurrentPushPivot.Remove(key);
        fromCurrentPushDir.Remove(key);
        fromCurrentPushStartDist.Remove(key);
        craneTruckFirstStart.Remove(key);
        craneTruckFirstRight.Remove(key);
        craneTruckFirstStart.Remove(key + "\na");
        craneTruckFirstRight.Remove(key + "\na");
        craneTruckFirstStart.Remove(key + "\nm");
        craneTruckFirstRight.Remove(key + "\nm");
    }
    /// <summary>
    /// 按 Clip 本地时间求相机世界位姿与可选 FOV。
    /// </summary>
    /// <param name="config">运镜配置。</param>
    /// <param name="playTiming">相对 Clip 内容起点的时间（秒）。</param>
    /// <param name="duration">Clip 时长；&lt;=0 时视为瞬间到终点。</param>
    /// <param name="selfUnit">施法者；空则用 fallbackSelfPos。</param>
    /// <param name="fallbackSelfPos">无 UnitMono 时的自身位置。</param>
    /// <param name="selfForward">施法者水平朝向参考（可非单位）。</param>
    /// <param name="targets">战场目标；可空。中点/目标枢轴用各目标的注视挂点。</param>
    /// <param name="mirror">是否做阵营镜像（位置翻 X）。</param>
    /// <param name="hasEntryPose">
    /// 是否提供起点机位。FromCurrentPose 为 true 时应为 true（由上一条模板终点 / 默认机位算出）；否则忽略 entry。
    /// </param>
    /// <param name="entryPos">起点世界坐标。</param>
    /// <param name="entryRot">起点世界旋转；FromCurrentPose 时从该朝向 Slerp 到 LookAt。</param>
    /// <param name="entryFov">起点 FOV；AnimateFov 且 FromCurrentPose 时作为起点。</param>
    /// <param name="poseOwnerId">定格「锁定第一帧机位」的缓存键前缀；编辑器传空，运行时传单位 Rid。</param>
    /// <param name="worldPos">输出机位。</param>
    /// <param name="worldRot">输出朝向。</param>
    /// <param name="fov">输出 FOV；未开 AnimateFov 时为 -1。</param>
    public static void Evaluate(
        TimelineClipCameraShotConfig config,
        float playTiming,
        float duration,
        UnitMono selfUnit,
        Vector3 fallbackSelfPos,
        Vector3 selfForward,
        IReadOnlyList<UnitMono> targets,
        bool mirror,
        bool hasEntryPose,
        Vector3 entryPos,
        Quaternion entryRot,
        float entryFov,
        string poseOwnerId,
        out Vector3 worldPos,
        out Quaternion worldRot,
        out float fov)
    {
        var lookNode = NormalizeLookAtNode(config.LookAtNodeType);
        var fromNode = NormalizeLookAtNode(config.LookAtFromNodeType);
        var selfPos = GetLookAtAnchorPos(selfUnit, lookNode, fallbackSelfPos);
        var targetPos = GetLookAtTargetPos(targets, lookNode, selfPos);
        var selfPosFrom = GetLookAtAnchorPos(selfUnit, fromNode, fallbackSelfPos);
        var targetPosFrom = GetLookAtTargetPos(targets, fromNode, selfPosFrom);

        // 阵营镜像：单位已在 TeamB 世界坐标，须先拉回 authoring 再加 PivotOffset，最后再镜像出去。
        // 禁止 Mirror(世界锚点+偏移)——会把已在对面的锚点再翻一次。
        ResolveLookBasisWorld(
            config,
            config.LookAtType,
            selfPos,
            targetPos,
            selfForward,
            mirror,
            out var pivot,
            out var facing);
        ResolveLookPivotWorld(
            config,
            config.LookAtFromType,
            selfPosFrom,
            targetPosFrom,
            mirror,
            out var pivotFrom);

        EvaluateResolved(config, playTiming, duration, pivot, pivotFrom, selfPosFrom, facing,
            mirror, hasEntryPose, entryPos, entryRot, entryFov, poseOwnerId,
            out worldPos, out worldRot, out fov);
    }

    /// <summary>从已解析的世界注视点和稳定构图轴求机位。预览与实战共用，不读取角色的相机跟随旋转。</summary>
    public static void EvaluateResolved(TimelineClipCameraShotConfig config, float playTiming, float duration,
        Vector3 pivot, Vector3 pivotFrom, Vector3 selfPosFrom, Vector3 facing, bool mirror,
        bool hasEntryPose, Vector3 entryPos, Quaternion entryRot, float entryFov, string poseOwnerId,
        out Vector3 worldPos, out Quaternion worldRot, out float fov, bool useCapturedShoulderAxis = false)
    {
        var t = duration <= 0.0001f ? 1f : Mathf.Clamp01(playTiming / duration);
        var eased = DOVirtual.EasedValue(0f, 1f, t, config.PathEase);
        // 勾「从当前机位起」：起点 = 上一条终点 / 默认机位，第 0 帧不跳。不勾：按模板参数摆绝对起点。
        var posePos = entryPos;
        var poseRot = entryRot;
        var poseFov = entryFov;
        var hasPose = hasEntryPose;
        var holdKeepEntry = config.ShotType == TimelineCameraShotType.Hold && config.HoldKeepEntryPose;
        if ((holdKeepEntry || (config.ShotType != TimelineCameraShotType.Hold && config.FromCurrentPose))
            && !hasPose)
        {
            GetDefaultBattleCameraPose(mirror, out posePos, out poseRot, out poseFov);
            hasPose = true;
        }

        var fromCurrent = config.FromCurrentPose && hasPose;
        if (config.ShotType == TimelineCameraShotType.Hold)
        {
            ResolveHoldLockedPivot(
                config,
                playTiming,
                poseOwnerId,
                mirror,
                ref pivot,
                ref hasPose,
                ref posePos);
            EvaluateHold(
                config,
                eased,
                mirror,
                pivot,
                hasPose,
                posePos,
                poseFov,
                out worldPos,
                out worldRot,
                out fov);
            return;
        }

        if (config.ShotType == TimelineCameraShotType.PivotBlend)
        {
            EvaluatePivotBlend(
                config,
                eased,
                fromCurrent,
                posePos,
                pivotFrom,
                pivot,
                facing,
                out worldPos,
                out worldRot);
            fov = EvaluateFov(config, eased, fromCurrent, poseFov);
            return;
        }

        if (config.ShotType == TimelineCameraShotType.WhipPan)
        {
            EvaluateWhipPan(
                config,
                eased,
                fromCurrent,
                mirror,
                posePos,
                poseRot,
                pivotFrom,
                pivot,
                facing,
                out worldPos,
                out worldRot);
            fov = EvaluateFov(config, eased, fromCurrent, poseFov);
            return;
        }

        if (config.ShotType == TimelineCameraShotType.Ots)
        {
            // 肩位已是世界坐标；过肩偏移在 authoring 里算再镜像，避免侧偏方向翻错。
            var shoulderAuth = TimelineTransformUtil.ToAuthoringPos(selfPosFrom, mirror);
            var lookAuth = useCapturedShoulderAxis
                ? shoulderAuth + TimelineTransformUtil.ToAuthoringPos(facing, mirror)
                : TimelineTransformUtil.ToAuthoringPos(pivot, mirror);
            var otsAuth = BuildOtsPosition(config, shoulderAuth, lookAuth);
            var otsPos = TimelineTransformUtil.FromAuthoringPos(otsAuth, mirror);
            EvaluateOts(
                config,
                eased,
                fromCurrent,
                posePos,
                poseRot,
                otsPos,
                pivot,
                out worldPos,
                out worldRot);
            fov = EvaluateFov(config, eased, fromCurrent, poseFov);
            return;
        }

        if (fromCurrent)
        {
            var lookPivot = pivot;
            switch (config.ShotType)
            {
                case TimelineCameraShotType.PushIn:
                case TimelineCameraShotType.DollyZoom:
                    worldPos = EvaluateDistanceOnly(
                        config,
                        playTiming,
                        poseOwnerId,
                        eased,
                        pivot,
                        facing,
                        posePos,
                        false,
                        out lookPivot);
                    break;
                case TimelineCameraShotType.PullOut:
                    worldPos = EvaluateDistanceOnly(
                        config,
                        playTiming,
                        poseOwnerId,
                        eased,
                        pivot,
                        facing,
                        posePos,
                        true,
                        out lookPivot);
                    break;
                case TimelineCameraShotType.BoomUp:
                case TimelineCameraShotType.BoomDown:
                    worldPos = EvaluateBoom(
                        config,
                        playTiming,
                        poseOwnerId,
                        eased,
                        pivot,
                        facing,
                        posePos,
                        out lookPivot);
                    break;
                case TimelineCameraShotType.Truck:
                    worldPos = EvaluateTruck(
                        config,
                        playTiming,
                        poseOwnerId,
                        eased,
                        mirror,
                        pivot,
                        facing,
                        posePos,
                        true);
                    break;
                case TimelineCameraShotType.Crane:
                    worldPos = EvaluateCrane(
                        config,
                        playTiming,
                        poseOwnerId,
                        eased,
                        mirror,
                        pivot,
                        facing,
                        posePos,
                        poseRot,
                        true);
                    break;
                case TimelineCameraShotType.DollyBoom:
                case TimelineCameraShotType.Helix:
                    worldPos = EvaluateHelix(config, eased, mirror, pivot, facing, posePos);
                    break;
                case TimelineCameraShotType.Orbit:
                default:
                    worldPos = EvaluatePositionFromCurrent(config, eased, mirror, pivot, facing, posePos);
                    break;
            }

            var lookRot = EvaluateRotation(worldPos, lookPivot, config.LockCameraUpright);
            worldRot = Quaternion.Slerp(poseRot, lookRot, eased);
            // 变焦推：始终驱动 FOV（距离↔FOV 对冲），不依赖 AnimateFov 勾选。
            fov = config.ShotType == TimelineCameraShotType.DollyZoom
                ? EvaluateFovForced(config, eased, fromCurrent, poseFov)
                : EvaluateFov(config, eased, fromCurrent, poseFov);
            return;
        }

        if (config.ShotType == TimelineCameraShotType.Truck)
        {
            worldPos = EvaluateTruck(
                config,
                playTiming,
                poseOwnerId,
                eased,
                mirror,
                pivot,
                facing,
                default,
                false);
            worldRot = EvaluateRotation(worldPos, pivot, config.LockCameraUpright);
            fov = EvaluateFov(config, eased, false, poseFov);
            return;
        }

        if (config.ShotType == TimelineCameraShotType.Crane)
        {
            worldPos = EvaluateCrane(
                config,
                playTiming,
                poseOwnerId,
                eased,
                mirror,
                pivot,
                facing,
                default,
                Quaternion.identity,
                false);
            worldRot = EvaluateRotation(worldPos, pivot, config.LockCameraUpright);
            fov = EvaluateFov(config, eased, false, poseFov);
            return;
        }

        worldPos = EvaluatePositionTemplate(config, eased, mirror, pivot, facing);
        worldRot = EvaluateRotation(worldPos, pivot, config.LockCameraUpright);
        fov = config.ShotType == TimelineCameraShotType.DollyZoom
            ? EvaluateFovForced(config, eased, false, poseFov)
            : EvaluateFov(config, eased, false, poseFov);
    }

    /// <summary>LookAt 类型是否需要战场目标。</summary>
    public static bool NeedsBattleTargets(TimelineCameraShotLookAtType lookAtType)
    {
        return lookAtType == TimelineCameraShotLookAtType.Target
            || lookAtType == TimelineCameraShotLookAtType.SelfTargetCenter;
    }

    /// <summary>运镜配置是否依赖战场目标（含换枢轴/甩镜的起枢轴；过肩始终需要目标）。</summary>
    public static bool NeedsBattleTargets(TimelineClipCameraShotConfig config)
    {
        if (config.ShotType == TimelineCameraShotType.Ots)
        {
            return true;
        }

        if (NeedsBattleTargets(config.LookAtType))
        {
            return true;
        }

        return (config.ShotType == TimelineCameraShotType.PivotBlend
                || (config.ShotType == TimelineCameraShotType.WhipPan && !config.FromCurrentPose))
            && NeedsBattleTargets(config.LookAtFromType);
    }

    /// <summary>
    /// 该类型是否锁起点（上一条运镜终点 / 默认机位），第 0 帧不按模板重摆。
    /// 环绕不锁，除非勾选 FromCurrentPose。
    /// </summary>
    public static bool LocksEntryPose(TimelineCameraShotType shotType)
    {
        switch (shotType)
        {
            case TimelineCameraShotType.PushIn:
            case TimelineCameraShotType.PullOut:
            case TimelineCameraShotType.BoomUp:
            case TimelineCameraShotType.BoomDown:
            case TimelineCameraShotType.DollyBoom:
            case TimelineCameraShotType.Truck:
            case TimelineCameraShotType.Helix:
            case TimelineCameraShotType.PivotBlend:
            case TimelineCameraShotType.Hold:
            case TimelineCameraShotType.WhipPan:
            case TimelineCameraShotType.DollyZoom:
            case TimelineCameraShotType.Ots:
            case TimelineCameraShotType.Crane:
                return true;
            case TimelineCameraShotType.Orbit:
            default:
                return false;
        }
    }

    /// <summary>
    /// 自身/目标参考点：按挂点取世界坐标；None 当 Transform（角色根）；缺节点回退 Transform。
    /// </summary>
    public static Vector3 GetLookAtAnchorPos(UnitMono unit, UnitNodeType nodeType)
    {
        return GetLookAtAnchorPos(unit, nodeType, Vector3.zero);
    }

    public static Vector3 GetLookAtAnchorPos(UnitMono unit, UnitNodeType nodeType, Vector3 fallbackPos)
    {
        if (unit == null)
        {
            return fallbackPos;
        }

        nodeType = NormalizeLookAtNode(nodeType);
        if (nodeType == UnitNodeType.UnitOriginPos)
        {
            return unit.BattleOriginPos;
        }

        return unit.GetNodeWorldPos(nodeType);
    }

    /// <summary>多个目标时取各目标注视挂点的平均中心。</summary>
    public static Vector3 GetLookAtAnchorCenter(
        IReadOnlyList<UnitMono> units,
        UnitNodeType nodeType)
    {
        var sum = Vector3.zero;
        var count = 0;
        for (var i = 0; i < units.Count; i++)
        {
            if (units[i] == null)
            {
                continue;
            }

            sum += GetLookAtAnchorPos(units[i], nodeType);
            count++;
        }

        return count > 0 ? sum / count : Vector3.zero;
    }

    public static Vector3 GetLookAtTargetPos(
        IReadOnlyList<UnitMono> targets,
        UnitNodeType nodeType,
        Vector3 selfPos)
    {
        if (targets == null || targets.Count == 0)
        {
            return selfPos;
        }

        return GetLookAtAnchorCenter(targets, nodeType);
    }

    private static UnitNodeType NormalizeLookAtNode(UnitNodeType nodeType)
    {
        return nodeType == UnitNodeType.None ? UnitNodeType.Transform : nodeType;
    }

    /// <summary>战斗默认机位（无上一条运镜时作 FromCurrent / 过渡起点）。</summary>
    public static void GetDefaultBattleCameraPose(out Vector3 worldPos, out Quaternion worldRot, out float fov)
    {
        GetDefaultBattleCameraPose(false, out worldPos, out worldRot, out fov);
    }

    /// <summary>
    /// 战斗默认机位。mirror 为 true 时按战场中线翻转（TeamB + 阵营镜像）。
    /// </summary>
    public static void GetDefaultBattleCameraPose(
        bool mirror,
        out Vector3 worldPos,
        out Quaternion worldRot,
        out float fov)
    {
        worldPos = BattleSharedConfig.Current.CameraPosition;
        worldRot = Quaternion.Euler(BattleSharedConfig.Current.CameraEuler);
        fov = BattleSharedConfig.Current.CameraFov;
        if (mirror)
        {
            worldPos = TimelineTransformUtil.MirrorBattleX(worldPos);
            worldRot = TimelineTransformUtil.MirrorBattleRotX(worldRot);
        }
    }

    /// <summary>
    /// 解析 FromCurrent 的入口 FOV。无效（≤0）时用 <see cref="BattleSharedConfig.CameraFov"/>，
    /// 禁止抓实时 Camera.fieldOfView（倒 scrub 时会被已改过的值污染）。
    /// </summary>
    public static float ResolveEntryFov(float entryFov)
    {
        return entryFov > 0f ? entryFov : BattleSharedConfig.Current.CameraFov;
    }

    /// <summary>
    /// 解析注视枢轴世界坐标（含 <see cref="TimelineClipCameraShotConfig.PivotOffset"/>，可选阵营镜像）。
    /// 定格且勾「锁定第一帧机位」时与采样一致：基点锁第 0 帧，偏移用当前值。
    /// 镜像时：世界锚点先 ToAuthoring，加偏移，再 FromAuthoring（勿 Mirror(世界锚点+偏移)）。
    /// </summary>
    public static Vector3 ResolveWorldLookPivot(
        TimelineClipCameraShotConfig config,
        bool isLookFrom,
        UnitMono selfUnit,
        Vector3 fallbackSelfPos,
        IReadOnlyList<UnitMono> targets,
        bool mirror,
        string poseOwnerId,
        float playTiming)
    {
        var lookAtType = isLookFrom ? config.LookAtFromType : config.LookAtType;
        var nodeType = isLookFrom ? config.LookAtFromNodeType : config.LookAtNodeType;
        var selfPos = GetLookAtAnchorPos(selfUnit, nodeType, fallbackSelfPos);
        var targetPos = GetLookAtTargetPos(targets, nodeType, selfPos);
        ResolveLookPivotWorld(config, lookAtType, selfPos, targetPos, mirror, out var pivot);

        if (!isLookFrom && config.ShotType == TimelineCameraShotType.Hold && config.HoldLockFirstFramePose)
        {
            var hasPose = false;
            var posePos = default(Vector3);
            ResolveHoldLockedPivot(
                config,
                playTiming,
                poseOwnerId,
                mirror,
                ref pivot,
                ref hasPose,
                ref posePos);
        }

        return pivot;
    }

    /// <summary>
    /// 注视枢轴：authoring 锚点 + PivotOffset，再按需镜像到世界。
    /// </summary>
    private static void ResolveLookPivotWorld(
        TimelineClipCameraShotConfig config,
        TimelineCameraShotLookAtType lookAtType,
        Vector3 worldSelfPos,
        Vector3 worldTargetPos,
        bool mirror,
        out Vector3 worldPivot)
    {
        var authSelf = TimelineTransformUtil.ToAuthoringPos(worldSelfPos, mirror);
        var authTarget = TimelineTransformUtil.ToAuthoringPos(worldTargetPos, mirror);
        var authPivot = ResolvePivot(config, lookAtType, authSelf, authTarget);
        worldPivot = TimelineTransformUtil.FromAuthoringPos(authPivot, mirror);
    }

    /// <summary>
    /// 注视枢轴 + 水平看向轴（均走 authoring 再镜像）。
    /// </summary>
    private static void ResolveLookBasisWorld(
        TimelineClipCameraShotConfig config,
        TimelineCameraShotLookAtType lookAtType,
        Vector3 worldSelfPos,
        Vector3 worldTargetPos,
        Vector3 worldSelfForward,
        bool mirror,
        out Vector3 worldPivot,
        out Vector3 worldFlatFacing)
    {
        var authSelf = TimelineTransformUtil.ToAuthoringPos(worldSelfPos, mirror);
        var authTarget = TimelineTransformUtil.ToAuthoringPos(worldTargetPos, mirror);
        var authForward = TimelineTransformUtil.ToAuthoringPos(worldSelfForward, mirror);
        var authPivot = ResolvePivot(config, lookAtType, authSelf, authTarget);
        var authFacing = ResolveFlatFacing(lookAtType, authSelf, authForward, authTarget);
        worldPivot = TimelineTransformUtil.FromAuthoringPos(authPivot, mirror);
        worldFlatFacing = TimelineTransformUtil.FromAuthoringPos(authFacing, mirror);
        worldFlatFacing.y = 0f;
        if (worldFlatFacing.sqrMagnitude < 1e-6f)
        {
            worldFlatFacing = Vector3.forward;
        }
        else
        {
            worldFlatFacing.Normalize();
        }
    }

    private static Vector3 ResolvePivot(
        TimelineClipCameraShotConfig config,
        TimelineCameraShotLookAtType lookAtType,
        Vector3 selfPos,
        Vector3 targetPos)
    {
        Vector3 pivot;
        switch (lookAtType)
        {
            case TimelineCameraShotLookAtType.Target:
                pivot = targetPos;
                break;
            case TimelineCameraShotLookAtType.SelfTargetCenter:
                pivot = (selfPos + targetPos) * 0.5f;
                break;
            case TimelineCameraShotLookAtType.Self:
            case TimelineCameraShotLookAtType.SelfOffset:
            default:
                pivot = selfPos;
                break;
        }

        return pivot + config.PivotOffset;
    }

    /// <summary>
    /// 水平「看向轴」：Target/中点用 施法者→目标；自身（含旧自身偏移）用实时 forward。
    /// 相机默认从轴的反方向（-facing）看向枢轴。
    /// </summary>
    private static Vector3 ResolveFlatFacing(
        TimelineCameraShotLookAtType lookAtType,
        Vector3 selfPos,
        Vector3 selfForward,
        Vector3 targetPos)
    {
        Vector3 facing;
        if (lookAtType == TimelineCameraShotLookAtType.Self
            || lookAtType == TimelineCameraShotLookAtType.SelfOffset)
        {
            facing = selfForward;
        }
        else
        {
            facing = targetPos - selfPos;
        }

        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f)
        {
            facing = selfForward;
            facing.y = 0f;
        }

        if (facing.sqrMagnitude < 1e-6f)
        {
            return Vector3.forward;
        }

        return facing.normalized;
    }

    /// <summary>
    /// 换枢轴：机位锁住（FromCurrent 用入口机位，否则相对终点枢轴摆绝对机位），
    /// LookAt 点从起始枢轴插到终点枢轴；朝向始终 LookRotation 到插值点。
    /// </summary>
    private static void EvaluatePivotBlend(
        TimelineClipCameraShotConfig config,
        float eased,
        bool fromCurrent,
        Vector3 entryPos,
        Vector3 pivotFrom,
        Vector3 pivotTo,
        Vector3 flatFacingTo,
        out Vector3 worldPos,
        out Quaternion worldRot)
    {
        if (fromCurrent)
        {
            worldPos = entryPos;
        }
        else
        {
            worldPos = pivotTo - flatFacingTo * Mathf.Max(config.Distance, 0.1f) + Vector3.up * config.Height;
        }

        var lookPoint = Vector3.Lerp(pivotFrom, pivotTo, eased);
        worldRot = EvaluateRotation(worldPos, lookPoint, config.LockCameraUpright);
    }

    /// <summary>
    /// 甩镜：机位锁住，朝向从起点旋转 Slerp 到「看向终点枢轴」。中间不盯注视点插值（与换枢轴不同）。
    /// FromCurrent：机位/起点朝向用入口机位；不勾：相对终点枢轴按 OrbitStartYaw + Distance + Height 摆绝对机位，
    /// 起点朝向 LookAt 起始枢轴。水平角 0 = 正后方（与旧「枢轴 - 看向轴」一致）。
    /// </summary>
    private static void EvaluateWhipPan(
        TimelineClipCameraShotConfig config,
        float eased,
        bool fromCurrent,
        bool mirror,
        Vector3 entryPos,
        Quaternion entryRot,
        Vector3 pivotFrom,
        Vector3 pivotTo,
        Vector3 flatFacingTo,
        out Vector3 worldPos,
        out Quaternion worldRot)
    {
        if (fromCurrent)
        {
            worldPos = entryPos;
        }
        else
        {
            worldPos = BuildOrbitPosition(
                pivotTo,
                flatFacingTo,
                MirrorYawDeg(config.OrbitStartYaw, mirror),
                Mathf.Max(config.Distance, 0.1f),
                config.Height);
        }

        var startRot = fromCurrent
            ? entryRot
            : EvaluateRotation(worldPos, pivotFrom, config.LockCameraUpright);
        var endRot = EvaluateRotation(worldPos, pivotTo, config.LockCameraUpright);
        worldRot = Quaternion.Slerp(startRot, endRot, eased);
    }

    /// <summary>
    /// 过肩：机位落在施法者肩后，始终看向注视枢轴。
    /// FromCurrent：从入口机位接到过肩构图；不勾：整段落在过肩机位。
    /// </summary>
    private static void EvaluateOts(
        TimelineClipCameraShotConfig config,
        float eased,
        bool fromCurrent,
        Vector3 entryPos,
        Quaternion entryRot,
        Vector3 otsPos,
        Vector3 lookPivot,
        out Vector3 worldPos,
        out Quaternion worldRot)
    {
        var otsRot = EvaluateRotation(otsPos, lookPivot, config.LockCameraUpright);
        if (fromCurrent)
        {
            worldPos = Vector3.Lerp(entryPos, otsPos, eased);
            worldRot = Quaternion.Slerp(entryRot, otsRot, eased);
            return;
        }

        worldPos = otsPos;
        worldRot = otsRot;
    }

    /// <summary>过肩绝对机位（authoring 空间）：肩位退后 + 肩侧 + 高度。</summary>
    private static Vector3 BuildOtsPosition(
        TimelineClipCameraShotConfig config,
        Vector3 shoulder,
        Vector3 lookPivot)
    {
        var toLook = lookPivot - shoulder;
        toLook.y = 0f;
        if (toLook.sqrMagnitude < 1e-6f)
        {
            toLook = Vector3.forward;
        }
        else
        {
            toLook.Normalize();
        }

        var back = -toLook;
        var right = Vector3.Cross(Vector3.up, toLook);
        if (right.sqrMagnitude < 1e-6f)
        {
            right = Vector3.right;
        }
        else
        {
            right.Normalize();
        }

        return shoulder
            + back * Mathf.Max(config.Distance, 0.05f)
            + right * config.MoveAmount
            + Vector3.up * config.Height;
    }

    /// <summary>
    /// 起重机：同时横移（MoveAmount）与升降（BoomAmount）。
    /// 横移沿<strong>起点机位自身右向</strong>（世界水平），不是绕枢轴的切向，避免看起来像环绕旋转。
    /// 第 0 帧只锁起点与右向；横移/升降量每帧读配置，改参立刻生效。
    /// FromCurrent：从入口机位走；不勾：相对枢轴按 OrbitStartYaw + Distance + Height 摆绝对起点（0=正后方），再横移+升降。
    /// 阵营镜像时 MoveAmount 翻号（与横移一致）。
    /// </summary>
    private static Vector3 EvaluateCrane(
        TimelineClipCameraShotConfig config,
        float playTiming,
        string poseOwnerId,
        float eased,
        bool mirror,
        Vector3 pivot,
        Vector3 flatFacing,
        Vector3 entryOrStand,
        Quaternion entryRot,
        bool fromCurrent)
    {
        Vector3 startPos;
        Vector3 right;
        if (!TryGetLockedCraneTruckBasis(poseOwnerId, config.ClipKey, mirror, playTiming, out startPos, out right))
        {
            if (fromCurrent)
            {
                startPos = entryOrStand;
                right = ResolveCameraFlatRight(entryRot, flatFacing);
            }
            else
            {
                startPos = BuildOrbitPosition(
                    pivot,
                    flatFacing,
                    MirrorYawDeg(config.OrbitStartYaw, mirror),
                    Mathf.Max(config.Distance, 0.1f),
                    config.Height);
                var startRot = EvaluateRotation(startPos, pivot, config.LockCameraUpright);
                right = ResolveCameraFlatRight(startRot, flatFacing);
            }

            LockCraneTruckBasis(poseOwnerId, config.ClipKey, mirror, playTiming, startPos, right);
        }

        // 枢轴/机位已在镜像世界；相机局部右 = −Mirror(编辑右)，横移须翻号才与 TeamA 同款构图。
        var lateral = mirror ? -config.MoveAmount : config.MoveAmount;
        var endPos = startPos + right * lateral + Vector3.up * config.BoomAmount;
        return Vector3.Lerp(startPos, endPos, eased);
    }

    /// <summary>
    /// 定格：停在机位上整段不移动，朝向盯住枢轴。
    /// 勾 <see cref="TimelineClipCameraShotConfig.HoldKeepEntryPose"/>：机位 = 进入机位。
    /// 勾 <see cref="TimelineClipCameraShotConfig.HoldLockFirstFramePose"/>：注视基点（及进入机位）锁在第 0 帧；
    /// 之后仍用当前 <see cref="TimelineClipCameraShotConfig.PivotOffset"/> / <see cref="TimelineClipCameraShotConfig.HoldOffset"/> 相对那份基点重算，改偏移可立刻生效。
    /// 不勾保持进入：机位 = 枢轴 + HoldOffset（世界轴；镜像翻 X）。FOV：保持进入机位时从入口 FOV 接到 FovEnd，否则 FovStart→FovEnd。
    /// </summary>
    private static void EvaluateHold(
        TimelineClipCameraShotConfig config,
        float eased,
        bool mirror,
        Vector3 pivot,
        bool hasEntryPose,
        Vector3 entryPos,
        float entryFov,
        out Vector3 worldPos,
        out Quaternion worldRot,
        out float fov)
    {
        if (config.HoldKeepEntryPose && hasEntryPose)
        {
            worldPos = entryPos;
            worldRot = EvaluateRotation(worldPos, pivot, config.LockCameraUpright);
            fov = EvaluateFov(config, eased, true, entryFov);
            return;
        }

        var offset = config.HoldOffset;
        if (mirror)
        {
            offset.x = -offset.x;
        }

        worldPos = pivot + offset;
        worldRot = EvaluateRotation(worldPos, pivot, config.LockCameraUpright);
        fov = EvaluateFov(config, eased, false, 0f);
    }

    /// <summary>
    /// 锁定第一帧机位：第 0 帧记下注视基点（枢轴减去当时的枢轴偏移；保持进入时也记下进入机位）。
    /// 之后基点不跟角色移动；枢轴偏移 / 机位坐标仍读当前配置叠上去，改完立刻重算。
    /// </summary>
    private static void ResolveHoldLockedPivot(
        TimelineClipCameraShotConfig config,
        float playTiming,
        string poseOwnerId,
        bool mirror,
        ref Vector3 pivot,
        ref bool hasPose,
        ref Vector3 posePos)
    {
        if (!config.HoldLockFirstFramePose)
        {
            return;
        }

        var key = MakeHoldPoseKey(poseOwnerId, config.ClipKey);
        if (key == null)
        {
            return;
        }

        var pivotOffset = config.LookPivot?.Offset ?? config.PivotOffset;
        if (mirror)
        {
            pivotOffset.x = -pivotOffset.x;
        }

        if (playTiming <= 0.0001f)
        {
            holdFirstLookBase[key] = pivot - pivotOffset;
            if (config.HoldKeepEntryPose && hasPose)
            {
                holdFirstEntryPos[key] = posePos;
            }
            else
            {
                holdFirstEntryPos.Remove(key);
            }

            return;
        }

        if (holdFirstLookBase.TryGetValue(key, out var frozenBase))
        {
            pivot = frozenBase + pivotOffset;
        }

        if (config.HoldKeepEntryPose && holdFirstEntryPos.TryGetValue(key, out var frozenEntry))
        {
            posePos = frozenEntry;
            hasPose = true;
        }
    }

    private static string MakeHoldPoseKey(string poseOwnerId, string clipKey)
    {
        if (string.IsNullOrEmpty(clipKey))
        {
            return null;
        }

        return string.IsNullOrEmpty(poseOwnerId) ? clipKey : poseOwnerId + "\n" + clipKey;
    }

    /// <summary>
    /// 推近：沿起点→第 0 帧枢轴的直线，把距离收到 <see cref="TimelineClipCameraShotConfig.Distance"/>。
    /// 拉远：在起点距离上再加 MoveAmount。枢轴与推近方向锁第 0 帧，整段是直线，不会绕。
    /// </summary>
    private static Vector3 EvaluateDistanceOnly(
        TimelineClipCameraShotConfig config,
        float playTiming,
        string poseOwnerId,
        float eased,
        Vector3 pivot,
        Vector3 flatFacing,
        Vector3 entryPos,
        bool pullOut,
        out Vector3 lookPivot)
    {
        ResolveFromCurrentPushRay(
            config,
            playTiming,
            poseOwnerId,
            pivot,
            entryPos,
            flatFacing,
            out lookPivot,
            out var dir,
            out var startDist);
        var endDist = pullOut
            ? startDist + Mathf.Abs(config.MoveAmount)
            : Mathf.Max(config.Distance, 0.1f);
        var dist = Mathf.Lerp(startDist, endDist, eased);
        return lookPivot + dir * dist;
    }

    /// <summary>升起 / 降下：推近射线锁第 0 帧，只改相对枢轴的高度。</summary>
    private static Vector3 EvaluateBoom(
        TimelineClipCameraShotConfig config,
        float playTiming,
        string poseOwnerId,
        float eased,
        Vector3 pivot,
        Vector3 flatFacing,
        Vector3 entryPos,
        out Vector3 lookPivot)
    {
        ResolveFromCurrentPushRay(
            config,
            playTiming,
            poseOwnerId,
            pivot,
            entryPos,
            flatFacing,
            out lookPivot,
            out var dir,
            out var startDist);
        var startPos = lookPivot + dir * startDist;
        var startHeight = startPos.y - lookPivot.y;
        var flat = new Vector3(startPos.x - lookPivot.x, 0f, startPos.z - lookPivot.z);
        var radius = Mathf.Max(flat.magnitude, 0.1f);
        var flatDir = flat.sqrMagnitude < 1e-6f ? new Vector3(dir.x, 0f, dir.z).normalized : flat / radius;
        if (flatDir.sqrMagnitude < 1e-6f)
        {
            flatDir = Vector3.back;
        }

        var delta = Mathf.Abs(config.MoveAmount);
        if (config.ShotType == TimelineCameraShotType.BoomDown)
        {
            delta = -delta;
        }

        var height = Mathf.Lerp(startHeight, startHeight + delta, eased);
        return lookPivot + flatDir * radius + Vector3.up * height;
    }

    /// <summary>
    /// 推近/拉远/升降：第 0 帧锁定枢轴与起点→枢轴方向（世界空间 3D），之后整段直线推近。
    /// </summary>
    private static void ResolveFromCurrentPushRay(
        TimelineClipCameraShotConfig config,
        float playTiming,
        string poseOwnerId,
        Vector3 pivot,
        Vector3 entryPos,
        Vector3 flatFacing,
        out Vector3 lockedPivot,
        out Vector3 dir,
        out float startDist)
    {
        lockedPivot = pivot;
        var offset = entryPos - pivot;
        startDist = offset.magnitude;
        if (startDist < 1e-6f)
        {
            dir = -flatFacing;
            if (dir.sqrMagnitude < 1e-6f)
            {
                dir = Vector3.back;
            }
            else
            {
                dir.Normalize();
            }

            startDist = 0.1f;
        }
        else
        {
            dir = offset / startDist;
        }

        var key = MakeHoldPoseKey(poseOwnerId, config.ClipKey);
        if (key == null)
        {
            return;
        }

        if (playTiming <= 0.0001f || !fromCurrentPushPivot.ContainsKey(key))
        {
            fromCurrentPushPivot[key] = lockedPivot;
            fromCurrentPushDir[key] = dir;
            fromCurrentPushStartDist[key] = startDist;
            return;
        }

        lockedPivot = fromCurrentPushPivot[key];
        dir = fromCurrentPushDir[key];
        startDist = fromCurrentPushStartDist[key];
    }

    /// <summary>
    /// 横移：沿看向枢轴时的左右方向平移。正 MoveAmount=向右；镜像时翻号。
    /// 第 0 帧只锁起点与右向；横移量每帧读配置，改参立刻生效。
    /// </summary>
    private static Vector3 EvaluateTruck(
        TimelineClipCameraShotConfig config,
        float playTiming,
        string poseOwnerId,
        float eased,
        bool mirror,
        Vector3 pivot,
        Vector3 flatFacing,
        Vector3 entryOrStand,
        bool fromCurrent)
    {
        Vector3 startPos;
        Vector3 right;
        if (!TryGetLockedCraneTruckBasis(poseOwnerId, config.ClipKey, mirror, playTiming, out startPos, out right))
        {
            if (fromCurrent)
            {
                startPos = entryOrStand;
            }
            else
            {
                startPos = pivot - flatFacing * Mathf.Max(config.Distance, 0.1f) + Vector3.up * config.Height;
            }

            right = ResolveTruckRight(startPos, pivot, flatFacing);
            LockCraneTruckBasis(poseOwnerId, config.ClipKey, mirror, playTiming, startPos, right);
        }

        // 与起重机相同：镜像世界下沿相机右向走须翻 MoveAmount。
        var amount = mirror ? -config.MoveAmount : config.MoveAmount;
        var endPos = startPos + right * amount;
        return Vector3.Lerp(startPos, endPos, eased);
    }

    /// <summary>看向枢轴时的右向（世界水平）。水平投影过小时回退 flatFacing。</summary>
    private static Vector3 ResolveTruckRight(Vector3 cameraPos, Vector3 pivot, Vector3 flatFacing)
    {
        var flat = cameraPos - pivot;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-6f)
        {
            flat = -flatFacing;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
            {
                flat = Vector3.back;
            }
        }

        var radial = flat.normalized;
        var right = Vector3.Cross(Vector3.up, -radial);
        if (right.sqrMagnitude < 1e-6f)
        {
            return Vector3.right;
        }

        return right.normalized;
    }

    /// <summary>机位自身右向压到世界水平面；退化时用看向轴的切向右。</summary>
    private static Vector3 ResolveCameraFlatRight(Quaternion cameraRot, Vector3 flatFacing)
    {
        var right = cameraRot * Vector3.right;
        right.y = 0f;
        if (right.sqrMagnitude >= 1e-6f)
        {
            return right.normalized;
        }

        var fallback = Vector3.Cross(Vector3.up, -flatFacing);
        if (fallback.sqrMagnitude < 1e-6f)
        {
            return Vector3.right;
        }

        return fallback.normalized;
    }

    private static bool TryGetLockedCraneTruckBasis(
        string poseOwnerId,
        string clipKey,
        bool mirror,
        float playTiming,
        out Vector3 startPos,
        out Vector3 right)
    {
        startPos = default;
        right = default;
        if (playTiming <= 0.0001f)
        {
            return false;
        }

        var key = MakeCraneTruckPoseKey(poseOwnerId, clipKey, mirror);
        if (key == null)
        {
            return false;
        }

        if (!craneTruckFirstStart.TryGetValue(key, out startPos)
            || !craneTruckFirstRight.TryGetValue(key, out right))
        {
            return false;
        }

        return true;
    }

    private static void LockCraneTruckBasis(
        string poseOwnerId,
        string clipKey,
        bool mirror,
        float playTiming,
        Vector3 startPos,
        Vector3 right)
    {
        var key = MakeCraneTruckPoseKey(poseOwnerId, clipKey, mirror);
        if (key == null)
        {
            return;
        }

        if (playTiming <= 0.0001f || !craneTruckFirstStart.ContainsKey(key))
        {
            craneTruckFirstStart[key] = startPos;
            craneTruckFirstRight[key] = right;
        }
    }

    /// <summary>横移/起重机路径缓存键：含 mirror，避免切 TeamB 后仍用 TeamA 锁点。</summary>
    private static string MakeCraneTruckPoseKey(string poseOwnerId, string clipKey, bool mirror)
    {
        var key = MakeHoldPoseKey(poseOwnerId, clipKey);
        if (key == null)
        {
            return null;
        }

        return mirror ? key + "\nm" : key + "\na";
    }

    /// <summary>螺旋 / 推拉升降环绕：从起点转水平角，同时把半径收到 Distance、高度加上 MoveAmount。环绕角为 0 则不转。</summary>
    private static Vector3 EvaluateHelix(
        TimelineClipCameraShotConfig config,
        float eased,
        bool mirror,
        Vector3 pivot,
        Vector3 flatFacing,
        Vector3 entryPos)
    {
        DecomposeAroundPivot(entryPos, pivot, flatFacing, out var startYaw, out var startRadius, out var startHeight);
        var yaw = Mathf.Lerp(startYaw, startYaw + MirrorYawDeg(config.OrbitAngle, mirror), eased);
        var radius = Mathf.Lerp(startRadius, Mathf.Max(config.Distance, 0.1f), eased);
        var height = Mathf.Lerp(startHeight, startHeight + config.MoveAmount, eased);
        return BuildOrbitPosition(pivot, flatFacing, yaw, radius, height);
    }

    /// <summary>
    /// 不勾「从当前机位起」时的绝对模板：推近/拉远沿看向轴；升降/横移用半径和高度；螺旋 / 推拉升降环绕同时改角、半径、高度。
    /// </summary>
    private static Vector3 EvaluatePositionTemplate(
        TimelineClipCameraShotConfig config,
        float eased,
        bool mirror,
        Vector3 pivot,
        Vector3 flatFacing)
    {
        switch (config.ShotType)
        {
            case TimelineCameraShotType.PushIn:
            case TimelineCameraShotType.DollyZoom:
            {
                var start = Mathf.Max(config.StartDistance, 0.1f);
                var end = Mathf.Max(config.Distance, 0.1f);
                var dist = Mathf.Lerp(start, end, eased);
                return pivot - flatFacing * dist + Vector3.up * config.Height;
            }
            case TimelineCameraShotType.PullOut:
            {
                var start = Mathf.Max(config.StartDistance, 0.1f);
                var end = start + Mathf.Abs(config.MoveAmount);
                var dist = Mathf.Lerp(start, end, eased);
                return pivot - flatFacing * dist + Vector3.up * config.Height;
            }
            case TimelineCameraShotType.BoomUp:
            case TimelineCameraShotType.BoomDown:
            {
                var delta = Mathf.Abs(config.MoveAmount);
                if (config.ShotType == TimelineCameraShotType.BoomDown)
                {
                    delta = -delta;
                }

                var height = Mathf.Lerp(config.Height, config.Height + delta, eased);
                return pivot - flatFacing * Mathf.Max(config.Distance, 0.1f) + Vector3.up * height;
            }
            case TimelineCameraShotType.DollyBoom:
            case TimelineCameraShotType.Helix:
            {
                var startYaw = MirrorYawDeg(config.OrbitStartYaw, mirror);
                var endYaw = startYaw + MirrorYawDeg(config.OrbitAngle, mirror);
                var yaw = Mathf.Lerp(startYaw, endYaw, eased);
                var radius = Mathf.Lerp(
                    Mathf.Max(config.StartDistance, 0.1f),
                    Mathf.Max(config.Distance, 0.1f),
                    eased);
                var height = Mathf.Lerp(config.Height, config.Height + config.MoveAmount, eased);
                return BuildOrbitPosition(pivot, flatFacing, yaw, radius, height);
            }
            case TimelineCameraShotType.PivotBlend:
                // 绝对机位：相对终点枢轴摆死；朝向由 EvaluatePivotBlend 处理
                return pivot - flatFacing * Mathf.Max(config.Distance, 0.1f) + Vector3.up * config.Height;
            case TimelineCameraShotType.WhipPan:
                return BuildOrbitPosition(
                    pivot,
                    flatFacing,
                    MirrorYawDeg(config.OrbitStartYaw, mirror),
                    Mathf.Max(config.Distance, 0.1f),
                    config.Height);
            case TimelineCameraShotType.Orbit:
            default:
            {
                var startYaw = MirrorYawDeg(config.OrbitStartYaw, mirror);
                var endYaw = startYaw + MirrorYawDeg(config.OrbitAngle, mirror);
                var yaw = Mathf.Lerp(startYaw, endYaw, eased);
                return BuildOrbitPosition(pivot, flatFacing, yaw, config.Distance, config.Height);
            }
        }
    }

    /// <summary>
    /// 从进条机位接到模板终点。
    /// PushIn：世界坐标插值到终点；Orbit：水平角/半径/高度从当前分解值插到（当前角+OrbitAngle / Distance / Height）。
    /// </summary>
    private static Vector3 EvaluatePositionFromCurrent(
        TimelineClipCameraShotConfig config,
        float eased,
        bool mirror,
        Vector3 pivot,
        Vector3 flatFacing,
        Vector3 entryPos)
    {
        switch (config.ShotType)
        {
            case TimelineCameraShotType.Orbit:
            {
                DecomposeAroundPivot(entryPos, pivot, flatFacing, out var startYaw, out var startRadius, out var startHeight);
                var endYaw = startYaw + MirrorYawDeg(config.OrbitAngle, mirror);
                var endRadius = Mathf.Max(config.Distance, 0.1f);
                var endHeight = config.Height;
                var yaw = Mathf.Lerp(startYaw, endYaw, eased);
                var radius = Mathf.Lerp(startRadius, endRadius, eased);
                var height = Mathf.Lerp(startHeight, endHeight, eased);
                return BuildOrbitPosition(pivot, flatFacing, yaw, radius, height);
            }
            case TimelineCameraShotType.PushIn:
            default:
            {
                // 沿起点→枢轴方向收近，不甩到「枢轴 - 看向轴」上（那会像绕过去）。
                var offset = entryPos - pivot;
                var startDist = offset.magnitude;
                if (startDist < 1e-6f)
                {
                    offset = -flatFacing;
                    if (offset.sqrMagnitude < 1e-6f)
                    {
                        offset = Vector3.back;
                    }

                    startDist = 0.1f;
                }

                var endDist = Mathf.Max(config.Distance, 0.1f);
                var dist = Mathf.Lerp(startDist, endDist, eased);
                return pivot + offset.normalized * dist;
            }
        }
    }

    /// <summary>
    /// 阵营镜像时水平角取反（与 <see cref="TimelineTransformUtil.MirrorBattleRotX"/> 的 yaw 取反对齐）。
    /// 枢轴/看向轴已走 authoring 镜像后，OrbitStartYaw / OrbitAngle 必须翻号，否则站位 Z 会反。
    /// </summary>
    private static float MirrorYawDeg(float yawDeg, bool mirror)
    {
        return mirror ? -yawDeg : yawDeg;
    }

    private static Vector3 BuildOrbitPosition(
        Vector3 pivot,
        Vector3 flatFacing,
        float yawDeg,
        float radius,
        float height)
    {
        var radial = Quaternion.AngleAxis(yawDeg, Vector3.up) * (-flatFacing);
        if (radial.sqrMagnitude < 1e-6f)
        {
            radial = -Vector3.forward;
        }
        else
        {
            radial.Normalize();
        }

        return pivot + radial * Mathf.Max(radius, 0.1f) + Vector3.up * height;
    }

    /// <summary>
    /// 将机位相对枢轴分解为：相对 -flatFacing 的水平角、水平半径、高度。
    /// </summary>
    private static void DecomposeAroundPivot(
        Vector3 cameraPos,
        Vector3 pivot,
        Vector3 flatFacing,
        out float yawDeg,
        out float radius,
        out float height)
    {
        var offset = cameraPos - pivot;
        height = offset.y;
        var flat = offset;
        flat.y = 0f;
        radius = Mathf.Max(flat.magnitude, 0.1f);

        var refDir = -flatFacing;
        refDir.y = 0f;
        if (refDir.sqrMagnitude < 1e-6f)
        {
            refDir = Vector3.forward;
        }
        else
        {
            refDir.Normalize();
        }

        if (flat.sqrMagnitude < 1e-6f)
        {
            yawDeg = 0f;
            return;
        }

        yawDeg = Vector3.SignedAngle(refDir, flat.normalized, Vector3.up);
    }

    private static Quaternion EvaluateRotation(Vector3 cameraPos, Vector3 pivot, bool lockUpright)
    {
        var look = pivot - cameraPos;
        if (look.sqrMagnitude < 1e-8f)
        {
            return Quaternion.identity;
        }

        // lockUpright：强制世界 up，避免荷兰角；关闭时同样用世界 up（预留后续 roll）
        _ = lockUpright;
        return Quaternion.LookRotation(look.normalized, Vector3.up);
    }

    internal static float EvaluateFov(
        TimelineClipCameraShotConfig config,
        float eased,
        bool useEntryFov,
        float entryFov)
    {
        if (!config.AnimateFov)
        {
            return -1f;
        }

        return EvaluateFovForced(config, eased, useEntryFov, entryFov);
    }

    /// <summary>
    /// 强制插值 FOV（变焦推始终走这里，不看 AnimateFov）。
    /// FromCurrent / 保持进入时从入口 FOV 接到 FovEnd；否则 FovStart→FovEnd。
    /// </summary>
    internal static float EvaluateFovForced(
        TimelineClipCameraShotConfig config,
        float eased,
        bool useEntryFov,
        float entryFov)
    {
        var start = useEntryFov ? ResolveEntryFov(entryFov) : config.FovStart;
        if (start <= 0f)
        {
            start = config.FovStart > 0f ? config.FovStart : BattleSharedConfig.Current.CameraFov;
        }

        return Mathf.Lerp(start, config.FovEnd, eased);
    }
}
