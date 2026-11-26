using UnityEditor;
using UnityEngine;

/// <summary>
/// 相机运动轨迹动画（编辑器预览）：
/// 进入 Clip 时把整棵 BattleCamera 挂到主角指定节点（保留世界位姿），再按 playTiming 采样 AnimationClip；
/// 离开时仅取消挂接，不恢复进 Clip 前机位。
/// </summary>
public class TimelineClipCameraPathAnim : TimelineClipBase
{
    private TimelineClipCameraPathAnimConfig thisConfig;
    private GameObject battleCameraGo;
    private Transform originalParent;
    private int originalSiblingIndex;
    private bool attached;
    private AnimationClip cachedAnimationClip;
    private string cachedAnimationClipPath;
    private float lastSampledPlayTiming = float.NaN;

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipCameraPathAnimConfig;
        base.Init(control, config);
        SyncDurationFromAnimationClip();
    }

    public override void DrawInspector()
    {
        var clipName = config != null ? UnitFeatureTypeDisplay.GetClipLabelName(config) : "Clip";
        EditorGUILayout.LabelField($"{clipName}参数配置", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("类型", UnitFeatureTypeDisplay.GetDisplayName(config.Type));
        config.ClipKey = EditorGUILayout.TextField("ClipKey", config.ClipKey ?? string.Empty);
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(this.config.PreKey));
        config.StartTime = EditorGUILayout.FloatField("开始时间", config.StartTime);
        EditorGUI.EndDisabledGroup();

        config.DelayTime = EditorGUILayout.FloatField("延迟时间", config.DelayTime);

        SyncDurationFromAnimationClip();
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.FloatField("时长", config.Duration);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.HelpBox("时长强制等于 AnimationClip.length，不可手动修改。", MessageType.None);

        DrawEndByOtherClipEndInspectorPublic();

        EditorGUI.BeginDisabledGroup(true);
        config.PreKey = EditorGUILayout.TextField("前置Key", config.PreKey);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Camera Path Anim", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "进入本 Clip：BattleCamera 整棵挂到主角指定节点（worldPositionStays，默认 Transform）。\n"
                + "按 AnimationClip 采样驱动 BattleCamera 根节点；离开仅取消挂接，不复位机位。\n"
                + "资源建议放在 Assets/Package_Unit/AnimationClip/。",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        var nextNode = TimelineEnumDisplay.DrawNodePopup("挂接节点", thisConfig.AttachNodeType);
        if (EditorGUI.EndChangeCheck())
        {
            thisConfig.AttachNodeType = nextNode;
            control.Timeline.StopPreView();
            control.Timeline.Repaint();
            SceneView.RepaintAll();
        }

        EditorGUI.BeginChangeCheck();
        thisConfig.MirrorByTeam = EditorGUILayout.Toggle("阵营镜像", thisConfig.MirrorByTeam);
        if (EditorGUI.EndChangeCheck())
        {
            // 同帧时间下提前 return 会跳过重采样；改开关后清缓存并停预览
            lastSampledPlayTiming = float.NaN;
            control.Timeline.StopPreView();
            control.Timeline.Repaint();
            SceneView.RepaintAll();
        }

        if (thisConfig.MirrorByTeam)
        {
            EditorGUILayout.HelpBox(
                "按 TeamA 侧录制的轨迹；TeamB 施法预览时在采样后对本地位姿翻 X（yaw 取反、roll 保持）。运行时 Animation.Play 后 LateUpdate 同样处理。",
                MessageType.Info);
        }

        var clipObj = string.IsNullOrEmpty(thisConfig.AnimationClipPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<AnimationClip>(thisConfig.AnimationClipPath);
        EditorGUI.BeginChangeCheck();
        var nextClip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", clipObj, typeof(AnimationClip), false);
        if (EditorGUI.EndChangeCheck())
        {
            thisConfig.AnimationClipPath = nextClip != null
                ? AssetDatabase.GetAssetPath(nextClip)
                : string.Empty;
            SyncDurationFromAnimationClip();
            control.Timeline.StopPreView();
            control.Timeline.Repaint();
            SceneView.RepaintAll();
        }

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("路径", thisConfig.AnimationClipPath ?? string.Empty);
        EditorGUI.EndDisabledGroup();
    }

    private void DrawEndByOtherClipEndInspectorPublic()
    {
        // 与基类 Catch UI 一致；时长仍强制跟 AnimationClip，不走 SyncClipDurationFromCatch
        if (control.IsWaitingEndByOtherClipCatch(this))
        {
            EditorGUILayout.HelpBox("Catch 模式：请在时间轴点击选择要绑定的 Clip", MessageType.Warning);
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(true);
        config.EndByOtherClipEnd = EditorGUILayout.TextField(
            "EndByOtherClipEnd",
            config.EndByOtherClipEnd ?? string.Empty);
        EditorGUI.EndDisabledGroup();
        if (GUILayout.Button("Catch", GUILayout.Width(64)))
        {
            control.BeginEndByOtherClipCatch(this);
        }

        EditorGUILayout.EndHorizontal();
    }

    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        return false;
    }

    public override void OnDestroy()
    {
        DetachFromCaster();
        base.OnDestroy();
    }

    public override void OnStopPreview()
    {
        DetachFromCaster();
        base.OnStopPreview();
    }

    protected override void OnLeftIn()
    {
        base.OnLeftIn();
        AttachToCaster();
        SampleAtPlayTiming();
    }

    protected override void OnInTimeLogic()
    {
        base.OnInTimeLogic();
        if (!attached)
        {
            AttachToCaster();
        }

        SampleAtPlayTiming();
    }

    protected override void OnLeftOut()
    {
        base.OnLeftOut();
        DetachFromCaster();
    }

    protected override void OnRightOut()
    {
        base.OnRightOut();
        DetachFromCaster();
    }

    protected override void OnOutTime()
    {
        base.OnOutTime();
        DetachFromCaster();
    }

    private void SyncDurationFromAnimationClip()
    {
        var clip = LoadAnimationClip();
        config.Duration = clip != null ? Mathf.Max(clip.length, 0f) : 0f;
    }

    private AnimationClip LoadAnimationClip()
    {
        if (thisConfig == null || string.IsNullOrEmpty(thisConfig.AnimationClipPath))
        {
            cachedAnimationClip = null;
            cachedAnimationClipPath = null;
            return null;
        }

        if (cachedAnimationClip != null
            && cachedAnimationClipPath == thisConfig.AnimationClipPath)
        {
            return cachedAnimationClip;
        }

        cachedAnimationClipPath = thisConfig.AnimationClipPath;
        cachedAnimationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(thisConfig.AnimationClipPath);
        return cachedAnimationClip;
    }

    private bool EnsureBattleCamera()
    {
        if (battleCameraGo != null)
        {
            return true;
        }

        battleCameraGo = EditorTimelineUtil.FindBattleCameraGameObject();
        if (battleCameraGo == null)
        {
            Debug.LogWarning("[TimelineClipCameraPathAnim] 未找到 BattleCamera。");
            return false;
        }

        return true;
    }

    private void AttachToCaster()
    {
        if (attached)
        {
            return;
        }

        if (!EnsureBattleCamera())
        {
            return;
        }

        var attachParent = ResolveAttachParent();
        if (attachParent == null)
        {
            Debug.LogWarning("[TimelineClipCameraPathAnim] 找不到挂接节点，无法挂接 BattleCamera。");
            return;
        }

        originalParent = battleCameraGo.transform.parent;
        originalSiblingIndex = battleCameraGo.transform.GetSiblingIndex();
        battleCameraGo.transform.SetParent(attachParent, true);
        attached = true;
    }

    /// <summary>解析挂接父节点；虚拟节点或找不到时回退到主角 Root / MyTransform。</summary>
    private Transform ResolveAttachParent()
    {
        if (control.MyTransform == null)
        {
            return null;
        }

        var unitMono = control.MyTransform.GetComponent<UnitMono>();
        var nodeType = thisConfig != null ? thisConfig.AttachNodeType : UnitNodeType.Transform;
        if (TimelineEditorUtil.IsVirtualOffsetNode(nodeType))
        {
            nodeType = UnitNodeType.Transform;
        }

        if (unitMono != null)
        {
            var node = unitMono.FindNodeTransform(nodeType);
            if (node != null)
            {
                return node;
            }

            return unitMono.Transform;
        }

        return control.MyTransform;
    }

    private void DetachFromCaster()
    {
        if (!attached || battleCameraGo == null)
        {
            attached = false;
            return;
        }

        // 取消挂接：挂回原父节点，保留当前世界位姿（不复位机位）
        battleCameraGo.transform.SetParent(originalParent, true);
        if (originalParent != null)
        {
            var childCount = originalParent.childCount;
            var sibling = Mathf.Clamp(originalSiblingIndex, 0, Mathf.Max(childCount - 1, 0));
            battleCameraGo.transform.SetSiblingIndex(sibling);
        }

        attached = false;
        originalParent = null;
        lastSampledPlayTiming = float.NaN;
    }

    private void SampleAtPlayTiming()
    {
        if (!EnsureBattleCamera())
        {
            return;
        }

        var clip = LoadAnimationClip();
        if (clip == null)
        {
            return;
        }

        SyncDurationFromAnimationClip();
        var t = Mathf.Clamp(playTiming, 0f, clip.length);
        if (!float.IsNaN(lastSampledPlayTiming) && Mathf.Abs(lastSampledPlayTiming - t) < 1e-5f)
        {
            return;
        }

        lastSampledPlayTiming = t;
        // 不用 AnimationMode：Stop 会回滚属性；这里要离开后保留最后一帧机位
        clip.SampleAnimation(battleCameraGo, t);
        if (ShouldMirrorPath())
        {
            TimelineTransformUtil.MirrorBattleLocalTransformX(battleCameraGo.transform);
        }
    }

    private bool ShouldMirrorPath()
    {
        if (thisConfig == null || !thisConfig.MirrorByTeam)
        {
            return false;
        }

        var unitMono = control.MyTransform != null
            ? control.MyTransform.GetComponent<UnitMono>()
            : null;
        var team = unitMono != null ? unitMono.TeamType : UnitTeamType.TeamA;
        return TimelineTransformUtil.NeedsTeamCameraMirror(thisConfig.MirrorByTeam, team);
    }
}
