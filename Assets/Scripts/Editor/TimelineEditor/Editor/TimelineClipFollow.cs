using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class TimelineClipFollow : TimelineClipBase
{
    private TimelineClipFollowConfig thisConfig;
    private GameObject effectInstance;
    private List<ParticleSystem> particleSystems;
    private List<ParticleSystem> particleSystemRoots;
    private Transform followerTrans;
    private Transform followTarget;
    private Vector3 leftSamplingPos;
    private Quaternion leftSamplingRot;
    private Vector3 followerToTargetOffset;
    private Vector3 lastPos;
    private Vector3 smoothVelocity;
    private float lastSampleRealtime;

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipFollowConfig;
        thisConfig.MigrateFollowLagFromLegacy();
        if (!string.IsNullOrEmpty(thisConfig.EffectPath))
        {
            thisConfig.EffectPath = EditorTimelineAssetPathUtil.ToEffectLogicalName(thisConfig.EffectPath);
        }

        base.Init(control, config);
        RefreshFollowTarget();
        EnsureFollower();
    }

    public override void DrawInspector()
    {
        base.DrawInspector();

        EditorGUILayout.LabelField("Follow", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        thisConfig.MoveEntityType = TimelineMoveTypeDisplay.DrawEntityPopup("跟随对象", thisConfig.MoveEntityType);
        if (thisConfig.MoveEntityType == TimelineMoveEntityType.Effect)
        {
            EditorTimelineUtil.DrawEffectPathField(ref thisConfig.EffectPath);
        }

        thisConfig.FollowNodeType = TimelineEnumDisplay.DrawNodePopup("跟随节点", thisConfig.FollowNodeType);

        thisConfig.FollowLagTime = Mathf.Max(
            0f,
            EditorGUILayout.FloatField("跟随滞后时间(秒)", thisConfig.FollowLagTime));
        EditorGUILayout.HelpBox(
            "0 = 瞬间跟上；数值越大跟随越慢（SmoothDamp）。\n跟随节点默认「根节点」。",
            MessageType.Info);

        if (EditorGUI.EndChangeCheck())
        {
            EditorTimelineUtil.ReleaseEditorEffectInstance(ref effectInstance, ref particleSystems, ref particleSystemRoots);
            followerTrans = null;
            RefreshFollowTarget();
            control.Timeline.Repaint();
        }
    }

    public override void OnDestroy()
    {
        EditorTimelineUtil.ReleaseEditorEffectInstance(ref effectInstance, ref particleSystems, ref particleSystemRoots);
        followerTrans = null;
        base.OnDestroy();
    }

    public override void OnStopPreview()
    {
        EditorTimelineUtil.ReleaseEditorEffectInstance(ref effectInstance, ref particleSystems, ref particleSystemRoots);
        followerTrans = null;
        smoothVelocity = Vector3.zero;
        base.OnStopPreview();
    }

    public override void OnPreviewQualityChanged()
    {
        EditorTimelineUtil.ReleaseEditorEffectInstance(ref effectInstance, ref particleSystems, ref particleSystemRoots);
        followerTrans = null;
        base.OnPreviewQualityChanged();
    }

    protected override void OnLeftIn()
    {
        EnsureFollower();
        if (followerTrans == null || followTarget == null)
        {
            base.OnLeftIn();
            return;
        }

        lastPos = followerTrans.position;
        leftSamplingPos = followerTrans.position;
        leftSamplingRot = followerTrans.rotation;
        followerToTargetOffset = followerTrans.position - followTarget.position;
        smoothVelocity = Vector3.zero;
        lastSampleRealtime = (float)EditorApplication.timeSinceStartup;
        base.OnLeftIn();
    }

    protected override void OnLeftOut()
    {
        base.OnLeftOut();
        if (followerTrans == null)
        {
            return;
        }

        followerTrans.position = leftSamplingPos;
        followerTrans.rotation = leftSamplingRot;
        smoothVelocity = Vector3.zero;
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
        RefreshFollowTarget();
        if (!EnsureFollower() || followTarget == null)
        {
            return;
        }

        ApplyFollow();
        lastPos = followerTrans.position;
    }

    /// <summary>按配置节点解析跟随目标；虚拟节点或找不到时回退 Root / MyTransform。</summary>
    private void RefreshFollowTarget()
    {
        followTarget = null;
        if (control.MyTransform == null)
        {
            return;
        }

        var unitMono = control.MyTransform.GetComponent<UnitMono>();
        var nodeType = thisConfig != null ? thisConfig.FollowNodeType : UnitNodeType.Root;
        if (TimelineEditorUtil.IsVirtualOffsetNode(nodeType))
        {
            nodeType = UnitNodeType.Root;
        }

        if (unitMono != null)
        {
            followTarget = unitMono.FindNodeTransform(nodeType);
            if (followTarget == null)
            {
                followTarget = unitMono.FindNodeTransform(UnitNodeType.Root);
            }

            if (followTarget == null)
            {
                followTarget = unitMono.Transform;
            }

            return;
        }

        followTarget = control.MyTransform;
    }

    private void ApplyFollow()
    {
        var idealPos = followTarget.position + followerToTargetOffset;
        var now = (float)EditorApplication.timeSinceStartup;
        var deltaTime = now - lastSampleRealtime;
        lastSampleRealtime = now;
        if (deltaTime <= 0f || deltaTime > 0.25f)
        {
            // 首帧 / 卡顿 / 跳播：按一小步推进，避免 SmoothDamp 爆炸
            deltaTime = 1f / 60f;
        }

        if (thisConfig.FollowLagTime <= 0f)
        {
            followerTrans.position = idealPos;
            smoothVelocity = Vector3.zero;
        }
        else
        {
            followerTrans.position = Vector3.SmoothDamp(
                lastPos,
                idealPos,
                ref smoothVelocity,
                thisConfig.FollowLagTime,
                Mathf.Infinity,
                deltaTime);
        }

        followerTrans.rotation = leftSamplingRot;

        if (thisConfig.MoveEntityType == TimelineMoveEntityType.Effect)
        {
            EditorTimelineUtil.SimulateEffectParticles(particleSystemRoots, playTiming);
        }
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
}
