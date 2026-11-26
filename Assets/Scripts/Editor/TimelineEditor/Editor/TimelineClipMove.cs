using System.Collections.Generic;
using DG.Tweening;
using UnityEditor;
using UnityEngine;

public class TimelineClipMove : TimelineClipBase
{
    /// <summary>逻辑时长为 0 时条上仍保留的可视厚度（秒），对齐施加力 Clip。</summary>
    private const float ZeroDurationVisualSeconds = 0.4f;

    private TimelineClipMoveConfig thisConfig;
    private GameObject effectInstance;
    private List<ParticleSystem> particleSystems;
    private List<ParticleSystem> particleSystemRoots;
    private Transform followerTrans;
    private Vector3 leftSamplingCameraPos;
    private Quaternion leftSamplingCameraRot;
    private bool leftSamplingCaptured;
    private float lerpDuration;
    private Tweener moveTween;
    private Tweener rotateTween;
    private Vector3 targetPos;
    private Quaternion targetRot;
    private Vector3 targetDir;
    /// <summary>
    /// 最终朝向线 B（终点沿 forward）与「旋转基点所在单位的本地 Z=0 平面」交点 P_B；
    /// 朝向自身→自身平面，朝向目标→目标平面，中点→中点处平均朝向平面。
    /// </summary>
    private Vector3 finalLookPlaneHitPB;
    private bool hasFinalLookPlaneHitPB;
    /// <summary>当前 moveTween / rotateTween 创建时使用的起点与时长，用于判断能否复用。</summary>
    private Vector3 tweenStartPos;
    private Quaternion tweenStartRot;
    private float tweenDuration;
    private Ease tweenEase;
    private Vector3 tweenEndPos;
    private Quaternion tweenEndRot;
    /// <summary>移动自身逻辑时长（不含定帧）；条上 Duration = 逻辑时长 + 与定帧交叉重叠。</summary>
    private float rawMoveDuration;
    private bool rawMoveDurationInitialized;
    private float lastWrittenDisplayDuration = -1f;

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipMoveConfig;
        if (!string.IsNullOrEmpty(thisConfig.EffectPath))
        {
            thisConfig.EffectPath = EditorTimelineAssetPathUtil.ToEffectLogicalName(thisConfig.EffectPath);
        }

        base.Init(control, config);
        EnsureFollower();
        rawMoveDurationInitialized = false;
        SyncMoveDurationsFromConfig();
    }

    public override void Draw()
    {
        ApplyDisplayDurationFromRaw();
        base.Draw();
    }

    /// <summary>快捷键改「移动逻辑时长」，再换算含定帧的展示条长；允许减到 0。</summary>
    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        if (config == null || frameDelta == 0 || frameStep <= 0f)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(config.EndByOtherClipEnd))
        {
            return false;
        }

        if (!rawMoveDurationInitialized)
        {
            SyncMoveDurationsFromConfig();
        }

        rawMoveDuration = SnapNudgeDurationAllowZero(rawMoveDuration, frameStep, frameDelta);
        rawMoveDurationInitialized = true;
        ApplyDisplayDurationFromRaw();
        return true;
    }

    /// <summary>逻辑时长为 0 时 Duration 也为 0，条宽按固定可视厚度绘制（同施加力）。</summary>
    protected override void SetRect()
    {
        if (config.Duration > 0f)
        {
            base.SetRect();
            return;
        }

        var y = control.Timeline.GetYByIndex(control.Clips.IndexOf(this));
        rect.x = control.Timeline.WorldPixelXToLocal(
            control.Timeline.TimeToPixel(config.StartTime + config.DelayTime));
        rect.y = control.Timeline.WorldPixelYToLocal(control.Timeline.YToPixel(y))
            + paddingRate / 2.0f * control.Timeline.GetItemPixelHeight();
        rect.width = control.Timeline.TimeToPixel(config.StartTime + ZeroDurationVisualSeconds)
            - control.Timeline.TimeToPixel(config.StartTime);
        rect.height = height;
    }

    public override void DrawInspector()
    {
        base.DrawInspector();

        if (thisConfig.AffectedByHitStop)
        {
            var expectedDisplay = TimelineEditorHitStopUtil.MeasureDisplayDurationForAnyHitStop(
                control,
                GetContentTimelineStartTime(),
                rawMoveDuration);
            if (!Mathf.Approximately(config.Duration, expectedDisplay)
                && !Mathf.Approximately(config.Duration, lastWrittenDisplayDuration))
            {
                rawMoveDuration = Mathf.Max(config.Duration, 0f);
                rawMoveDurationInitialized = true;
            }
        }
        else if (!Mathf.Approximately(config.Duration, lastWrittenDisplayDuration))
        {
            rawMoveDuration = Mathf.Max(config.Duration, 0f);
            rawMoveDurationInitialized = true;
        }

        ApplyDisplayDurationFromRaw();

        EditorGUILayout.LabelField("Move", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            thisConfig.AffectedByHitStop
                ? "条长 = 移动逻辑时长 + 与「角色定帧」交叉重叠的时间（只加算交集）。\n"
                    + "逻辑时长为 0 时瞬间到位，条上仍保留可视厚度。\n"
                    + "选中本 Clip 后，Scene 会画「起点→终点」虚线轨迹与箭头；可拖终点球/轴向改平移偏移，拖圆弧改旋转偏移。"
                    + (thisConfig.MoveEntityType == TimelineMoveEntityType.Camera
                        ? "\n移动对象为相机时，Scene 右下角有「终点构图预览」小窗（不挪动真实相机）。"
                        : string.Empty)
                : "未勾选「受定帧影响」：条长 = 移动逻辑时长，采样跟全局时间轴。\n"
                    + "逻辑时长为 0 时瞬间到位，条上仍保留可视厚度。\n"
                    + "选中本 Clip 后，Scene 会画「起点→终点」虚线轨迹与箭头；可拖终点球/轴向改平移偏移，拖圆弧改旋转偏移。"
                    + (thisConfig.MoveEntityType == TimelineMoveEntityType.Camera
                        ? "\n移动对象为相机时，Scene 右下角有「终点构图预览」小窗（不挪动真实相机）。"
                        : string.Empty),
            MessageType.Info);

        thisConfig.AffectedByHitStop = DrawAffectedByHitStopToggle(thisConfig.AffectedByHitStop);
        ApplyDisplayDurationFromRaw();

        EditorGUI.BeginChangeCheck();
        rawMoveDuration = EditorGUILayout.FloatField("移动逻辑时长", Mathf.Max(rawMoveDuration, 0f));
        if (EditorGUI.EndChangeCheck())
        {
            rawMoveDuration = Mathf.Max(rawMoveDuration, 0f);
            rawMoveDurationInitialized = true;
            ApplyDisplayDurationFromRaw();
            control.Timeline.StopPreView();
            control.Timeline.Repaint();
        }

        if (thisConfig.AffectedByHitStop)
        {
            var overlap = TimelineEditorHitStopUtil.GetOverlapWithAnyHitStop(
                control,
                GetContentTimelineStartTime(),
                rawMoveDuration);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.FloatField("与定帧交叉重叠", overlap);
            EditorGUILayout.FloatField("展示条长", config.Duration);
            EditorGUI.EndDisabledGroup();
        }
        else
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.FloatField("展示条长", config.Duration);
            EditorGUI.EndDisabledGroup();
        }

        EditorGUI.BeginChangeCheck();
        thisConfig.MoveEntityType = TimelineMoveTypeDisplay.DrawEntityPopup("移动对象", thisConfig.MoveEntityType);
        if (thisConfig.MoveEntityType == TimelineMoveEntityType.Effect)
        {
            EditorTimelineUtil.DrawEffectPathField(ref thisConfig.EffectPath);
        }
        thisConfig.MoveToEase = (Ease)EditorGUILayout.EnumPopup("变换缓动", thisConfig.MoveToEase);
        if (thisConfig.MoveEntityType == TimelineMoveEntityType.Camera)
        {
            thisConfig.MirrorByTeam = EditorGUILayout.Toggle("阵营镜像", thisConfig.MirrorByTeam);
            if (thisConfig.MirrorByTeam)
            {
                EditorGUILayout.HelpBox(
                    "按 TeamA（左、朝右）编辑构图；TeamB 施法时将相机落点/朝向按战场中线镜像。",
                    MessageType.Info);
            }

            thisConfig.LockCameraUpright = EditorGUILayout.Toggle("锁定水平(禁止歪斜)", thisConfig.LockCameraUpright);
            if (thisConfig.LockCameraUpright)
            {
                // 勾选后清掉已有 roll，避免旧数据继续歪
                if (Mathf.Abs(thisConfig.OffsetEuler.z) > 0.001f)
                {
                    thisConfig.OffsetEuler.z = 0f;
                }

                EditorGUILayout.HelpBox(
                    "勾选后 Scene 旋转环/自由球不会写入歪斜（OffsetEuler.z=0），最终朝向以世界朝上为 up。",
                    MessageType.Info);
            }
        }
        else
        {
            thisConfig.MirrorByTeam = false;
        }

        EditorGUI.BeginChangeCheck();
        var nextTranslate = TimelineMoveTypeDisplay.DrawTranslatePopup("平移类型", thisConfig.TranslateMovePointType);
        if (EditorGUI.EndChangeCheck())
        {
            TimelineEditorTargetUtil.TryAssignTranslateMovePointType(control, thisConfig, nextTranslate);
        }

        TimelineEditorTargetUtil.DrawTargetDependencyHelp(
            control,
            TimelineEditorTargetUtil.NeedsBattleTargets(thisConfig.TranslateMovePointType));
        thisConfig.OffestPos = EditorGUILayout.Vector3Field("平移偏移位置", thisConfig.OffestPos);

        EditorGUI.BeginChangeCheck();
        var nextRotate = TimelineMoveTypeDisplay.DrawRotatePopup("旋转类型", thisConfig.RotateMovePointType);
        if (EditorGUI.EndChangeCheck())
        {
            TimelineEditorTargetUtil.TryAssignRotateMovePointType(control, thisConfig, nextRotate);
        }

        TimelineEditorTargetUtil.DrawTargetDependencyHelp(
            control,
            TimelineEditorTargetUtil.NeedsBattleTargets(thisConfig.RotateMovePointType));
        thisConfig.OffsetEuler = EditorGUILayout.Vector3Field("旋转偏移欧拉角", thisConfig.OffsetEuler);
        if (IsCameraUprightLockActive())
        {
            thisConfig.OffsetEuler.z = 0f;
        }
        thisConfig.FastApproachFinalEuler = EditorGUILayout.Toggle(
            "快速追近最终欧拉角",
            thisConfig.FastApproachFinalEuler);
        if (thisConfig.FastApproachFinalEuler)
        {
            thisConfig.FastApproachFinalEulerSpeed = Mathf.Clamp01(
                EditorGUILayout.Slider("追近速度", thisConfig.FastApproachFinalEulerSpeed, 0f, 1f));
            EditorGUILayout.HelpBox(
                "旋转始终朝向红点 P_B（相对当前机位）。\n"
                    + "P_B = 朝向线 ∩ 旋转基点平面（自身/目标/中点按类型）。\n"
                    + "追近速度 0=与不勾选相同（整段按缓动渐渐朝向红点）；1=全程盯红点。",
                MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "旋转按 Duration + 变换缓动，从起点渐渐朝向红点 P_B（相对当前机位）。\n"
                    + "P_B = 朝向线 ∩ 旋转基点平面（自身/目标/中点按类型）。",
                MessageType.None);
        }
        if (EditorGUI.EndChangeCheck())
        {
            EditorTimelineUtil.ReleaseEditorEffectInstance(ref effectInstance, ref particleSystems, ref particleSystemRoots);
            followerTrans = null;
            ApplyDisplayDurationFromRaw();
            control.Timeline.Repaint();
            SceneView.RepaintAll();
        }
    }

    public override void OnDestroy()
    {
        KillLerpTweens();
        EditorTimelineUtil.ReleaseEditorEffectInstance(ref effectInstance, ref particleSystems, ref particleSystemRoots);
        followerTrans = null;
        base.OnDestroy();
    }

    public override void OnPreviewQualityChanged()
    {
        KillLerpTweens();
        EditorTimelineUtil.ReleaseEditorEffectInstance(ref effectInstance, ref particleSystems, ref particleSystemRoots);
        followerTrans = null;
        base.OnPreviewQualityChanged();
    }

    public override void OnStopPreview()
    {
        KillLerpTweens();
        EditorTimelineUtil.ReleaseEditorEffectInstance(ref effectInstance, ref particleSystems, ref particleSystemRoots);
        followerTrans = null;
        leftSamplingCaptured = false;
        base.OnStopPreview();
    }

    protected override void OnLeftIn()
    {
        EnsureFollower();
        if (followerTrans == null)
        {
            base.OnLeftIn();
            return;
        }

        leftSamplingCameraPos = followerTrans.position;
        leftSamplingCameraRot = followerTrans.rotation;
        leftSamplingCaptured = true;
        base.OnLeftIn();
    }

    protected override void OnLeftOut()
    {
        base.OnLeftOut();
        KillLerpTweens();
        if (followerTrans == null)
        {
            return;
        }

        followerTrans.position = leftSamplingCameraPos;
        followerTrans.rotation = leftSamplingCameraRot;
        GoTo();
    }

    protected override void OnRightOut()
    {
        base.OnRightOut();
        GoTo();
    }

    protected override void OnInTimeLogic()
    {
        base.OnInTimeLogic();
        GoTo();
    }

    private void GoTo()
    {
        if (!EnsureFollower())
        {
            return;
        }

        EnsureLerpTweens();
    }

    private void EnsureLerpTweens()
    {
        ApplyDisplayDurationFromRaw();
        RefreshMoveTargets();

        if (!IsFinite(targetPos) || !IsFinite(targetRot))
        {
            targetPos = leftSamplingCameraPos;
            targetRot = leftSamplingCameraRot;
            targetDir = targetRot * Vector3.forward;
            return;
        }

        // 逻辑时长 0：瞬间到位（条宽仅 UI 可视厚度，不参与插值）
        if (rawMoveDuration <= 0f)
        {
            KillLerpTweens();
            followerTrans.SetPositionAndRotation(targetPos, targetRot);
            if (thisConfig.MoveEntityType == TimelineMoveEntityType.Effect)
            {
                EditorTimelineUtil.SimulateEffectParticles(particleSystemRoots, playTiming);
            }

            return;
        }

        lerpDuration = rawMoveDuration;
        var sampleTiming = thisConfig.AffectedByHitStop
            ? TimelineEditorHitStopUtil.GetClipLogicPlayTimingForAnyHitStopClippedToRaw(
                control,
                GetContentTimelineStartTime(),
                rawMoveDuration,
                (float)control.Timeline.RunningTime)
            : playTiming;
        if (sampleTiming < 0f)
        {
            sampleTiming = 0f;
        }

        var t = Mathf.Clamp(sampleTiming, 0f, lerpDuration);
        var canReuse = moveTween != null
            && rotateTween != null
            && moveTween.IsActive()
            && rotateTween.IsActive()
            && Mathf.Approximately(tweenDuration, lerpDuration)
            && tweenEase == thisConfig.MoveToEase
            && (tweenStartPos - leftSamplingCameraPos).sqrMagnitude < 1e-8f
            && Quaternion.Angle(tweenStartRot, leftSamplingCameraRot) < 0.01f;

        if (!canReuse)
        {
            KillLerpTweens();
            followerTrans.SetPositionAndRotation(leftSamplingCameraPos, leftSamplingCameraRot);
            tweenStartPos = leftSamplingCameraPos;
            tweenStartRot = leftSamplingCameraRot;
            tweenDuration = lerpDuration;
            tweenEase = thisConfig.MoveToEase;
            moveTween = followerTrans
                .DOMove(targetPos, lerpDuration)
                .SetEase(thisConfig.MoveToEase)
                .Pause();
            rotateTween = followerTrans
                .DORotateQuaternion(targetRot, lerpDuration)
                .SetEase(thisConfig.MoveToEase)
                .Pause();
            tweenEndPos = targetPos;
            tweenEndRot = targetRot;
        }
        else if ((tweenEndPos - targetPos).sqrMagnitude > 1e-8f)
        {
            moveTween.ChangeEndValue(targetPos, false);
            tweenEndPos = targetPos;
        }

        moveTween.Goto(t, true);
        ApplyRotationSample(t, lerpDuration);

        if (thisConfig.MoveEntityType == TimelineMoveEntityType.Effect)
        {
            // 粒子仍跟全局 playTiming，定帧不冻特效
            EditorTimelineUtil.SimulateEffectParticles(particleSystemRoots, playTiming);
        }
    }

    /// <summary>
    /// 有 P_B 时：旋转终点始终是「当前机位看向红点」，按缓动从起点插过去。
    /// 不勾选 / 追近速度 0：采样时间与位移相同；勾选且速度&gt;0 时压缩采样（更快盯上红点）。
    /// </summary>
    private void ApplyRotationSample(float positionT, float duration)
    {
        if (hasFinalLookPlaneHitPB)
        {
            var preferredUp = IsCameraUprightLockActive() ? Vector3.up : targetRot * Vector3.up;
            var lookNow = ComputeLookRotationTowardPB(followerTrans.position, preferredUp);
            var rotateT = ResolveRotateSampleTiming(positionT, duration);
            if (Quaternion.Angle(tweenEndRot, lookNow) > 0.01f || !rotateTween.IsActive())
            {
                rotateTween.ChangeEndValue(lookNow, false);
                tweenEndRot = lookNow;
            }

            rotateTween.Goto(rotateT, true);
            return;
        }

        if (Quaternion.Angle(tweenEndRot, targetRot) > 0.01f)
        {
            rotateTween.ChangeEndValue(targetRot, false);
            tweenEndRot = targetRot;
        }

        rotateTween.Goto(positionT, true);
    }

    /// <summary>从 <paramref name="fromPos"/> 看向 P_B 的旋转。</summary>
    private Quaternion ComputeLookRotationTowardPB(Vector3 fromPos, Vector3 preferredUp)
    {
        var look = finalLookPlaneHitPB - fromPos;
        if (look.sqrMagnitude < 1e-8f)
        {
            return targetRot;
        }

        var up = preferredUp.sqrMagnitude > 1e-8f ? preferredUp : Vector3.up;
        return Quaternion.LookRotation(look.normalized, up);
    }

    /// <summary>
    /// 不勾选或速度为 0：与位置同一采样时间（整段 DOTween 渐渐朝向红点）；
    /// 勾选且速度&gt;0：压缩时间，更快看向红点。
    /// </summary>
    private float ResolveRotateSampleTiming(float positionT, float duration)
    {
        if (!thisConfig.FastApproachFinalEuler || duration <= 0f)
        {
            return positionT;
        }

        var speed = Mathf.Clamp01(thisConfig.FastApproachFinalEulerSpeed);
        if (speed <= 1e-5f)
        {
            return positionT;
        }

        if (speed >= 1f - 1e-5f)
        {
            return duration;
        }

        return Mathf.Clamp(positionT / (1f - speed), 0f, duration);
    }

    private void SyncMoveDurationsFromConfig()
    {
        var contentStart = GetContentTimelineStartTime();
        if (thisConfig.AffectedByHitStop)
        {
            rawMoveDuration = TimelineEditorHitStopUtil.GetRawDurationForAnyHitStop(
                control,
                contentStart,
                Mathf.Max(config.Duration, 0f));
        }
        else
        {
            rawMoveDuration = Mathf.Max(config.Duration, 0f);
        }

        rawMoveDuration = Mathf.Max(rawMoveDuration, 0f);
        rawMoveDurationInitialized = true;
        ApplyDisplayDurationFromRaw();
    }

    private void ApplyDisplayDurationFromRaw()
    {
        if (!rawMoveDurationInitialized)
        {
            SyncMoveDurationsFromConfig();
            return;
        }

        rawMoveDuration = Mathf.Max(rawMoveDuration, 0f);
        if (thisConfig.AffectedByHitStop)
        {
            config.Duration = TimelineEditorHitStopUtil.MeasureDisplayDurationForAnyHitStop(
                control,
                GetContentTimelineStartTime(),
                rawMoveDuration);
        }
        else
        {
            config.Duration = rawMoveDuration;
        }

        lastWrittenDisplayDuration = config.Duration;
        lerpDuration = rawMoveDuration;
    }

    /// <summary>根据当前配置重算终点位置 / 朝向（预览 Tween、Gizmos、Scene 手柄共用）。</summary>
    private void RefreshMoveTargets()
    {
        SetTranslateTarget(thisConfig.TranslateMovePointType);
        SetRotateTarget(thisConfig.RotateMovePointType);
    }

    private void SetRotateTarget(RotateMovePointType pointType)
    {
        hasFinalLookPlaneHitPB = false;
        if (pointType == RotateMovePointType.None)
        {
            targetRot = leftSamplingCameraRot;
            targetDir = targetRot * Vector3.forward;
            return;
        }

        if (!TryGetRotateLookDir(pointType, out var dir, out var mirror))
        {
            targetRot = leftSamplingCameraRot;
            targetDir = targetRot * Vector3.forward;
            return;
        }

        var authoringRot = ApplyRotateOffset(
            Quaternion.LookRotation(dir),
            dir,
            ResolveOffsetEulerForApply());
        targetRot = TimelineTransformUtil.FromAuthoringRot(authoringRot, mirror);
        if (IsCameraUprightLockActive())
        {
            targetRot = LevelRotationKeepWorldUp(targetRot);
        }

        targetDir = targetRot * Vector3.forward;
        // 终点朝向改为看向 P_B（朝向线 ∩ 旋转基点所在平面）
        ApplyFinalLookTowardPlaneHitPB();
    }

    /// <summary>
    /// 线 B：过终点 targetPos、沿 targetDir；
    /// 平面：旋转基点处的本地 Z=0（朝向自身=自身，朝向目标=目标，中点=中点+平均朝向）。
    /// 交点 P_B 为最终注视点，终点旋转改为 LookAt(P_B)。
    /// </summary>
    private void ApplyFinalLookTowardPlaneHitPB()
    {
        if (!IsFinite(targetPos) || !IsFinite(targetDir))
        {
            return;
        }

        if (targetDir.sqrMagnitude < 1e-8f)
        {
            return;
        }

        if (!TryGetRotateBasePlane(thisConfig.RotateMovePointType, out var planeOrigin, out var planeNormal))
        {
            return;
        }

        if (!TryIntersectLineWithPlane(targetPos, targetDir.normalized, planeOrigin, planeNormal.normalized, out var pb))
        {
            return;
        }

        if (!IsFinite(pb))
        {
            return;
        }

        finalLookPlaneHitPB = pb;
        hasFinalLookPlaneHitPB = true;

        var look = pb - targetPos;
        if (look.sqrMagnitude < 1e-8f)
        {
            return;
        }

        Vector3 up;
        if (IsCameraUprightLockActive())
        {
            up = Vector3.up;
        }
        else
        {
            up = targetRot * Vector3.up;
            if (up.sqrMagnitude < 1e-8f)
            {
                up = Vector3.up;
            }
        }

        targetRot = Quaternion.LookRotation(look.normalized, up);
        targetDir = targetRot * Vector3.forward;
    }

    /// <summary>
    /// 按旋转类型取 P_B 求交平面：原点=旋转基点，法线=对应单位 forward（中点为平均 forward）。
    /// </summary>
    private bool TryGetRotateBasePlane(RotateMovePointType pointType, out Vector3 planeOrigin, out Vector3 planeNormal)
    {
        planeOrigin = Vector3.zero;
        planeNormal = Vector3.forward;
        switch (pointType)
        {
            case RotateMovePointType.RotateToSelf:
                if (control.MyTransform == null)
                {
                    return false;
                }

                planeOrigin = control.MyTransform.position;
                planeNormal = control.MyTransform.forward;
                return planeNormal.sqrMagnitude >= 1e-8f;
            case RotateMovePointType.RotateToTarget:
            {
                if (!TimelineEditorTargetUtil.TryGetTargetsForAuthoring(
                        control,
                        GetContentTimelineStartTime(),
                        out var targets,
                        "Move.RotateToTarget.Plane",
                        logWarning: false)
                    || targets.Count == 0)
                {
                    return false;
                }

                if (!TimelineEditorTargetUtil.TryGetTeamCenter(targets, out planeOrigin))
                {
                    return false;
                }

                planeNormal = AverageUnitForward(targets);
                return planeNormal.sqrMagnitude >= 1e-8f;
            }
            case RotateMovePointType.RotateToSelfTargetCenter:
            {
                if (control.MyTransform == null)
                {
                    return false;
                }

                if (!TimelineEditorTargetUtil.TryGetTargetsForAuthoring(
                        control,
                        GetContentTimelineStartTime(),
                        out var targets,
                        "Move.RotateToSelfTargetCenter.Plane",
                        logWarning: false)
                    || targets.Count == 0)
                {
                    return false;
                }

                if (!TimelineEditorTargetUtil.TryGetTeamCenter(targets, out var targetCenter))
                {
                    return false;
                }

                planeOrigin = (control.MyTransform.position + targetCenter) * 0.5f;
                var selfFwd = control.MyTransform.forward;
                var targetFwd = AverageUnitForward(targets);
                planeNormal = selfFwd.normalized + targetFwd.normalized;
                if (planeNormal.sqrMagnitude < 1e-8f)
                {
                    planeNormal = selfFwd;
                }

                return planeNormal.sqrMagnitude >= 1e-8f;
            }
            default:
                return false;
        }
    }

    private static Vector3 AverageUnitForward(IReadOnlyList<UnitMono> units)
    {
        var sum = Vector3.zero;
        for (var i = 0; i < units.Count; i++)
        {
            var fwd = units[i].Transform.forward;
            if (fwd.sqrMagnitude >= 1e-8f)
            {
                sum += fwd.normalized;
            }
        }

        return sum;
    }

    /// <summary>无限直线与平面求交；平行且不在平面上时失败。</summary>
    private static bool TryIntersectLineWithPlane(
        Vector3 lineOrigin,
        Vector3 lineDirUnit,
        Vector3 planeOrigin,
        Vector3 planeNormalUnit,
        out Vector3 hit)
    {
        hit = Vector3.zero;
        var denom = Vector3.Dot(lineDirUnit, planeNormalUnit);
        if (Mathf.Abs(denom) < 1e-6f)
        {
            return false;
        }

        var t = Vector3.Dot(planeOrigin - lineOrigin, planeNormalUnit) / denom;
        hit = lineOrigin + lineDirUnit * t;
        return true;
    }

    /// <summary>旋转基点世界坐标（自身 / 目标中心 / 中点）。</summary>
    private bool TryGetRotateLookAtWorldPos(RotateMovePointType pointType, out Vector3 worldLookAt)
    {
        worldLookAt = Vector3.zero;
        switch (pointType)
        {
            case RotateMovePointType.RotateToSelf:
                if (control.MyTransform == null)
                {
                    return false;
                }

                worldLookAt = control.MyTransform.position;
                return true;
            case RotateMovePointType.RotateToTarget:
                return TimelineEditorTargetUtil.TryGetBattleTargetCenterForAuthoring(
                    control,
                    GetContentTimelineStartTime(),
                    out worldLookAt,
                    "Move.RotateToTarget",
                    logWarning: false);
            case RotateMovePointType.RotateToSelfTargetCenter:
                if (control.MyTransform == null)
                {
                    return false;
                }

                if (!TimelineEditorTargetUtil.TryGetBattleTargetCenterForAuthoring(
                    control,
                    GetContentTimelineStartTime(),
                    out var targetCenter,
                    "Move.RotateToSelfTargetCenter",
                    logWarning: false))
                {
                    return false;
                }

                worldLookAt = (control.MyTransform.position + targetCenter) * 0.5f;
                return true;
            default:
                return false;
        }
    }

    /// <summary>求旋转基准朝向（编辑空间 look dir），以及是否处于阵营镜像。</summary>
    private bool TryGetRotateLookDir(RotateMovePointType pointType, out Vector3 authoringDir, out bool mirror)
    {
        authoringDir = Vector3.forward;
        mirror = UseTeamMirror();
        if (!TryGetRotateLookAtWorldPos(pointType, out var worldLookAt))
        {
            return false;
        }

        var rotateToPos = TimelineTransformUtil.ToAuthoringPos(worldLookAt, mirror);
        var authoringCamPos = TimelineTransformUtil.ToAuthoringPos(targetPos, mirror);
        authoringDir = rotateToPos - authoringCamPos;
        if (authoringDir.sqrMagnitude >= 1e-8f)
        {
            return true;
        }

        // 终点叠在旋转基点上时，改用「起点→旋转基点」兜底，避免朝向静默退回旧相机旋转
        var authoringStart = TimelineTransformUtil.ToAuthoringPos(leftSamplingCameraPos, mirror);
        authoringDir = rotateToPos - authoringStart;
        return authoringDir.sqrMagnitude >= 1e-8f;
    }

    /// <summary>与运行时一致：绕 up / 侧向 / 水平朝向依次叠加 OffsetEuler。</summary>
    private static Quaternion ApplyRotateOffset(Quaternion baseLook, Vector3 dir, Vector3 offsetEuler)
    {
        var q1 = Quaternion.AngleAxis(offsetEuler.x, Vector3.up);
        var pitchAxis = Vector3.Cross(dir, Vector3.up);
        if (pitchAxis.sqrMagnitude < 1e-8f)
        {
            pitchAxis = Vector3.right;
        }

        var q2 = Quaternion.AngleAxis(offsetEuler.y, pitchAxis);
        var rollAxis = new Vector3(dir.x, 0f, dir.z);
        if (rollAxis.sqrMagnitude < 1e-8f)
        {
            rollAxis = Vector3.forward;
        }

        var q3 = Quaternion.AngleAxis(offsetEuler.z, rollAxis);
        return q3 * q2 * q1 * baseLook;
    }

    /// <summary>从期望朝向反推 OffsetEuler，使 <see cref="ApplyRotateOffset"/> 结果贴近 desired。</summary>
    private static Vector3 ExtractOffsetEuler(Quaternion desiredAuthoringRot, Vector3 dir)
    {
        var baseLook = Quaternion.LookRotation(dir);
        var pitchAxis = Vector3.Cross(dir, Vector3.up);
        if (pitchAxis.sqrMagnitude < 1e-8f)
        {
            pitchAxis = Vector3.right;
        }
        else
        {
            pitchAxis.Normalize();
        }

        var rollAxis = new Vector3(dir.x, 0f, dir.z);
        if (rollAxis.sqrMagnitude < 1e-8f)
        {
            rollAxis = Vector3.forward;
        }
        else
        {
            rollAxis.Normalize();
        }

        var desireFwd = desiredAuthoringRot * Vector3.forward;
        var desireUp = desiredAuthoringRot * Vector3.up;

        var cur = baseLook;
        var curFwd = cur * Vector3.forward;
        var a = Vector3.ProjectOnPlane(curFwd, Vector3.up);
        var b = Vector3.ProjectOnPlane(desireFwd, Vector3.up);
        var x = (a.sqrMagnitude > 1e-8f && b.sqrMagnitude > 1e-8f)
            ? Vector3.SignedAngle(a, b, Vector3.up)
            : 0f;
        cur = Quaternion.AngleAxis(x, Vector3.up) * cur;

        var y = Vector3.SignedAngle(cur * Vector3.forward, desireFwd, pitchAxis);
        cur = Quaternion.AngleAxis(y, pitchAxis) * cur;

        var z = Vector3.SignedAngle(cur * Vector3.up, desireUp, rollAxis);
        return new Vector3(x, y, z);
    }

    private void SetTranslateTarget(TranslateMovePointType pointType)
    {
        if (!TryGetTranslateAnchor(pointType, out var origin, out var basisRot, out var mirror))
        {
            targetPos = leftSamplingCameraPos;
            return;
        }

        var authoringPos = origin + basisRot * thisConfig.OffestPos;
        targetPos = TimelineTransformUtil.FromAuthoringPos(authoringPos, mirror);
    }

    /// <summary>
    /// 平移偏移所在局部空间：origin + basisRot * OffestPos（均为编辑空间）。
    /// </summary>
    private bool TryGetTranslateAnchor(
        TranslateMovePointType pointType,
        out Vector3 authoringOrigin,
        out Quaternion authoringBasisRot,
        out bool mirror)
    {
        authoringOrigin = Vector3.zero;
        authoringBasisRot = Quaternion.identity;
        mirror = UseTeamMirror();

        switch (pointType)
        {
            case TranslateMovePointType.TranslateToSelf:
            {
                if (control.MyTransform == null)
                {
                    return false;
                }

                authoringOrigin = TimelineTransformUtil.ToAuthoringPos(control.MyTransform.position, mirror);
                authoringBasisRot = TimelineTransformUtil.ToAuthoringRot(control.MyTransform.rotation, mirror);
                return true;
            }
            case TranslateMovePointType.TranslateToTarget:
            {
                if (!TimelineEditorTargetUtil.TryGetTargetsForAuthoring(
                    control,
                    GetContentTimelineStartTime(),
                    out var targets,
                    "Move.TranslateToTarget",
                    logWarning: false))
                {
                    return false;
                }

                var firstTarget = targets[0];
                authoringOrigin = TimelineTransformUtil.ToAuthoringPos(
                    TimelineTransformUtil.GetTeamCenter(targets),
                    mirror);
                authoringBasisRot = TimelineTransformUtil.ToAuthoringRot(firstTarget.Transform.rotation, mirror);
                return true;
            }
            case TranslateMovePointType.TranslateToSelfTargetCenter:
            {
                if (control.MyTransform == null)
                {
                    return false;
                }

                if (!TimelineEditorTargetUtil.TryGetBattleTargetCenterForAuthoring(
                    control,
                    GetContentTimelineStartTime(),
                    out var endPointRaw,
                    "Move.TranslateToSelfTargetCenter",
                    logWarning: false))
                {
                    return false;
                }

                var endPoint = TimelineTransformUtil.ToAuthoringPos(endPointRaw, mirror);
                var selfPos = TimelineTransformUtil.ToAuthoringPos(control.MyTransform.position, mirror);
                authoringOrigin = (endPoint + selfPos) * 0.5f;
                var dir = authoringOrigin - selfPos;
                if (dir.sqrMagnitude < 1e-8f)
                {
                    authoringBasisRot = Quaternion.identity;
                }
                else
                {
                    authoringBasisRot = Quaternion.LookRotation(dir);
                }

                return true;
            }
            case TranslateMovePointType.None:
            default:
                return false;
        }
    }

    bool UseTeamMirror()
    {
        if (thisConfig.MoveEntityType != TimelineMoveEntityType.Camera)
        {
            return false;
        }

        var unitMono = control.MyTransform != null
            ? control.MyTransform.GetComponent<UnitMono>()
            : null;
        var team = unitMono != null ? unitMono.TeamType : UnitTeamType.TeamA;
        return TimelineTransformUtil.NeedsTeamCameraMirror(thisConfig.MirrorByTeam, team);
    }

    private void KillLerpTweens()
    {
        if (moveTween != null)
        {
            moveTween.Kill(false);
            moveTween = null;
        }

        if (rotateTween != null)
        {
            rotateTween.Kill(false);
            rotateTween = null;
        }

        tweenDuration = -1f;
    }

    private bool EnsureFollower()
    {
        if (thisConfig.MoveEntityType == TimelineMoveEntityType.Camera)
        {
            return EditorTimelineUtil.TryGetCameraFollowerTransform(out followerTrans);
        }

        followerTrans = EditorTimelineUtil.EnsureEditorEffectInstance(ref effectInstance, thisConfig.EffectPath);
        if (followerTrans != null && (particleSystemRoots == null || particleSystemRoots.Count == 0))
        {
            EditorTimelineUtil.SetupEffectParticlePreview(effectInstance, ref particleSystems, ref particleSystemRoots);
        }

        return followerTrans != null;
    }

    /// <summary>Scene / Gizmos 绘制前保证有可用的采样起点与 follower。</summary>
    private bool PrepareSceneEditState()
    {
        if (!EnsureFollower())
        {
            return false;
        }

        if (!leftSamplingCaptured)
        {
            leftSamplingCameraPos = followerTrans.position;
            leftSamplingCameraRot = followerTrans.rotation;
        }

        RefreshMoveTargets();
        return IsFinite(targetPos);
    }

    static bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
            || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }

    static bool IsFinite(Quaternion q)
    {
        return !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w)
            || float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));
    }

    public override void OnDrawGizmos()
    {
        base.OnDrawGizmos();
        // 轨迹与手柄统一在 OnSceneGUI 用 Handles 画（虚线/标签在 Gizmos 里不明显）
    }

    public override void OnSceneGUI(SceneView sceneView)
    {
        base.OnSceneGUI(sceneView);
        if (!PrepareSceneEditState())
        {
            return;
        }

        // 避免 Scene 点击被选物体/框选吃掉，保证手柄能抢到热控
        var suppressId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(suppressId);

        var startPos = leftSamplingCameraPos;
        if (!IsFinite(startPos))
        {
            startPos = followerTrans.position;
        }

        // 运动轨迹：粗实线底 + 起点→终点缓慢流动虚线；朝向喇叭对准旋转基点拉长
        var lookAtWorld = targetPos;
        var hasLookAt = thisConfig.RotateMovePointType != RotateMovePointType.None
            && TryGetRotateLookAtWorldPos(thisConfig.RotateMovePointType, out lookAtWorld);
        if (!hasLookAt)
        {
            lookAtWorld = targetPos;
        }

        EditorTimelineUtil.DrawMoveTrajectory(
            startPos,
            targetPos,
            targetDir,
            new Color(0.15f, 1f, 0.4f, 1f),
            new Color(0.3f, 0.8f, 1f, 1f),
            new Color(0.2f, 1f, 0.35f, 1f),
            hasLookAt,
            lookAtWorld);

        // 最终注视点 P_B：朝向线 ∩ 旋转基点平面
        if (hasFinalLookPlaneHitPB && IsFinite(finalLookPlaneHitPB))
        {
            var pbSize = HandleUtility.GetHandleSize(finalLookPlaneHitPB) * 0.14f;
            Handles.color = Color.red;
            Handles.SphereHandleCap(0, finalLookPlaneHitPB, Quaternion.identity, pbSize, EventType.Repaint);
            Handles.Label(
                finalLookPlaneHitPB + Vector3.up * HandleUtility.GetHandleSize(finalLookPlaneHitPB) * 0.22f,
                "最终注视点");
            Handles.color = new Color(1f, 0.2f, 0.2f, 0.85f);
            Handles.DrawDottedLine(targetPos, finalLookPlaneHitPB, 4f);
        }

        // 相机移动：Scene 右下角小窗预览终点构图（不挪真实 BattleCamera）
        if (thisConfig.MoveEntityType == TimelineMoveEntityType.Camera && IsFinite(targetRot))
        {
            EditorTimelineUtil.DrawBattleCameraEndPoseScenePreview(sceneView, targetPos, targetRot);
        }

        // 轨迹虚线动画不强制 Scene 死循环 Repaint（编辑器 update 在播放/ scrub 时会刷）
        var changed = false;
        var handleSize = HandleUtility.GetHandleSize(targetPos);

        if (thisConfig.TranslateMovePointType == TranslateMovePointType.None)
        {
            Handles.Label(targetPos + Vector3.up * handleSize * 0.35f, "平移类型为「无」：无法拖位置偏移");
        }
        else if (!TryGetTranslateAnchor(thisConfig.TranslateMovePointType, out var origin, out var basisRot, out var mirror))
        {
            Handles.color = Color.yellow;
            Handles.Label(
                targetPos + Vector3.up * handleSize * 0.4f,
                TimelineEditorTargetUtil.NeedsBattleTargets(thisConfig.TranslateMovePointType)
                    ? TimelineEditorTargetUtil.GetAuthoringTargetFailureHint(control, GetContentTimelineStartTime())
                    : "当前平移类型无法计算锚点");
        }
        else
        {
            // 世界坐标平移手柄（轴对齐世界 XYZ，便于调 OffestPos）
            var markerColor = new Color(0.3f, 0.8f, 1f, 1f);
            Handles.color = markerColor;
            Handles.Label(
                targetPos + Vector3.up * handleSize * 0.28f,
                "平移偏移(世界)");
            EditorGUI.BeginChangeCheck();
            var newWorldPos = Handles.FreeMoveHandle(
                targetPos,
                handleSize * 0.16f,
                Vector3.zero,
                Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck() && IsFinite(newWorldPos))
            {
                var authoringPos = TimelineTransformUtil.ToAuthoringPos(newWorldPos, mirror);
                thisConfig.OffestPos = Quaternion.Inverse(basisRot) * (authoringPos - origin);
                RefreshMoveTargets();
                changed = true;
            }

            EditorGUI.BeginChangeCheck();
            // 强制世界轴向，不用终点 targetRot（局部轴）
            newWorldPos = Handles.PositionHandle(targetPos, Quaternion.identity);
            if (EditorGUI.EndChangeCheck() && IsFinite(newWorldPos))
            {
                var authoringPos = TimelineTransformUtil.ToAuthoringPos(newWorldPos, mirror);
                thisConfig.OffestPos = Quaternion.Inverse(basisRot) * (authoringPos - origin);
                RefreshMoveTargets();
                changed = true;
            }
        }

        if (thisConfig.RotateMovePointType == RotateMovePointType.None)
        {
            if (thisConfig.TranslateMovePointType == TranslateMovePointType.None)
            {
                Handles.Label(targetPos, "旋转类型为「无」：无法拖旋转偏移");
            }
        }
        else if (!TryGetRotateLookDir(thisConfig.RotateMovePointType, out var lookDir, out var rotMirror))
        {
            Handles.color = Color.yellow;
            var rotateHint = TimelineEditorTargetUtil.NeedsBattleTargets(thisConfig.RotateMovePointType)
                ? TimelineEditorTargetUtil.GetAuthoringTargetFailureHint(control, GetContentTimelineStartTime())
                : "旋转朝向无法计算（落点与旋转基点可能重合）";
            Handles.Label(targetPos + Vector3.up * handleSize * 0.62f, rotateHint);
        }
        else
        {
            RefreshMoveTargets();
            if (!TryGetRotateLookDir(thisConfig.RotateMovePointType, out lookDir, out rotMirror))
            {
                if (changed)
                {
                    AfterSceneHandleChanged();
                }

                return;
            }

            Handles.Label(
                targetPos + Vector3.up * handleSize * 0.5f,
                "旋转偏移(世界轴)");
            EditorGUI.BeginChangeCheck();
            var newWorldRot = DoWorldAxisRotationHandle(targetRot, targetPos);
            if (EditorGUI.EndChangeCheck() && IsFinite(newWorldRot))
            {
                if (IsCameraUprightLockActive())
                {
                    newWorldRot = LevelRotationKeepWorldUp(newWorldRot);
                }

                var authoringRot = TimelineTransformUtil.ToAuthoringRot(newWorldRot, rotMirror);
                thisConfig.OffsetEuler = ExtractOffsetEuler(authoringRot, lookDir);
                if (IsCameraUprightLockActive())
                {
                    thisConfig.OffsetEuler.z = 0f;
                }

                changed = true;
            }
        }

        if (changed)
        {
            AfterSceneHandleChanged();
        }
    }

    /// <summary>相机且勾选锁定水平时，应用旋转偏移前强制 roll=0。</summary>
    private Vector3 ResolveOffsetEulerForApply()
    {
        var euler = thisConfig.OffsetEuler;
        if (IsCameraUprightLockActive())
        {
            euler.z = 0f;
        }

        return euler;
    }

    /// <summary>移动对象为相机且勾选「锁定水平」。</summary>
    private bool IsCameraUprightLockActive()
    {
        return thisConfig.MoveEntityType == TimelineMoveEntityType.Camera
            && thisConfig.LockCameraUpright;
    }

    /// <summary>保留朝向，用世界 up 去 roll，避免荷兰角。</summary>
    private static Quaternion LevelRotationKeepWorldUp(Quaternion worldRot)
    {
        var forward = worldRot * Vector3.forward;
        if (forward.sqrMagnitude < 1e-8f)
        {
            return worldRot;
        }

        forward.Normalize();
        // 几乎竖直朝上/朝下时 LookRotation(fwd, up) 不稳定，略抬水平分量
        if (Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.999f)
        {
            forward = (forward + Vector3.forward * 0.001f).normalized;
        }

        return Quaternion.LookRotation(forward, Vector3.up);
    }

    /// <summary>
    /// 世界坐标旋转手柄：绕世界 X/Y/Z 圆环拖动，回写为终点世界旋转后再反推 OffsetEuler。
    /// </summary>
    private static Quaternion DoWorldAxisRotationHandle(Quaternion worldRot, Vector3 position)
    {
        var size = HandleUtility.GetHandleSize(position);
        var newRot = worldRot;
        var oldColor = Handles.color;

        Handles.color = Handles.xAxisColor;
        newRot = Handles.Disc(newRot, position, Vector3.right, size, true, EditorSnapSettings.rotate);
        Handles.color = Handles.yAxisColor;
        newRot = Handles.Disc(newRot, position, Vector3.up, size, true, EditorSnapSettings.rotate);
        Handles.color = Handles.zAxisColor;
        newRot = Handles.Disc(newRot, position, Vector3.forward, size, true, EditorSnapSettings.rotate);
        Handles.color = Handles.centerColor;
        newRot = Handles.FreeRotateHandle(newRot, position, size * 0.2f);

        Handles.color = oldColor;
        return newRot;
    }

    private void AfterSceneHandleChanged()
    {
        RefreshMoveTargets();
        if (followerTrans != null && (moveTween != null || rotateTween != null))
        {
            GoTo();
        }

        control.Timeline.Repaint();
        SceneView.RepaintAll();
    }
}
