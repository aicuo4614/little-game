using DG.Tweening;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

public class TimelineClipPlayBullet : TimelineClipBase
{
    private TimelineClipPlayBulletConfig thisConfig;
    private UnitMono sourceUnitMono;
    private List<UnitMono> targetUnitMonos = new List<UnitMono>();
    private List<BulletEntity> bulletEntitys = new List<BulletEntity>();
    private List<GameObject> bulletInstances = new List<GameObject>();
    private List<float> flyDurations = new List<float>();
    /// <summary>每个子弹实例对应的根粒子列表；编辑器预览用 Simulate，与 PlayEffect 一致。</summary>
    private List<List<ParticleSystem>> bulletParticleRoots = new List<List<ParticleSystem>>();

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipPlayBulletConfig;
        if (!string.IsNullOrEmpty(thisConfig.Path))
        {
            thisConfig.Path = EditorTimelineAssetPathUtil.ToEffectLogicalName(thisConfig.Path);
            prefabPath = thisConfig.Path;
        }

        base.Init(control, config);
        EnsureNodeData();
        RefreshFlyDuration();
    }

    public override void Draw()
    {
        ResolveSourceUnitMono();
        ResolveTargetUnitMono();
        RefreshFlyDuration();

        if (flyDurations.Count > 0)
        {
            config.Duration = flyDurations.Max();
        }


        base.Draw();
    }

    /// <summary>时长由飞行距离/速度计算，快捷键不可改。</summary>
    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        return false;
    }

    public override void DrawInspector()
    {
        base.DrawInspector();

        EditorGUILayout.LabelField("Play Bullet", EditorStyles.boldLabel);
        TimelineEditorUtil.DrawStableHelp(
            "Scene 可拖起点位置/旋转/缩放。延迟移动：先停起点再飞。起点重置旋转/缩放与普通特效相同。");
        EditorGUI.BeginChangeCheck();
        var nextBulletPath = EditorGUILayout.TextField("逻辑名", thisConfig.Path ?? string.Empty);
        if (EditorGUI.EndChangeCheck())
        {
            thisConfig.Path = nextBulletPath;
        }

        var prefabObj = EditorTimelineAssetPathUtil.LoadEffectPrefab(thisConfig.Path);
        var newPrefab = (GameObject)EditorGUILayout.ObjectField("子弹 Prefab", prefabObj, typeof(GameObject), false);
        if (newPrefab != null)
        {
            var logical = EditorTimelineAssetPathUtil.EffectLogicalFromAssetPath(AssetDatabase.GetAssetPath(newPrefab));
            if (logical != prefabPath)
            {
                ApplyBulletPath(logical);
            }
        }

        EditorGUI.BeginChangeCheck();
        thisConfig.MoveSpeed = EditorGUILayout.FloatField("飞行速度", thisConfig.MoveSpeed);
        thisConfig.MoveDelayTime = Mathf.Max(
            0f,
            EditorGUILayout.FloatField("延迟移动时间", thisConfig.MoveDelayTime));
        thisConfig.TrajectoryType = TimelineEnumDisplay.DrawBulletTrajectoryPopup(
            "弹道类型",
            thisConfig.TrajectoryType);
        DrawTrajectoryTypeParams();
        thisConfig.FacingType = (TimelineBulletFacingType)EditorGUILayout.Popup("视觉朝向", (int)thisConfig.FacingType,
            new[] { "沿三维轨迹（原有）", "面向相机并沿飞行方向旋转" });
        if (thisConfig.FacingType == TimelineBulletFacingType.CameraBillboard)
        {
            thisConfig.BillboardAngleOffset = EditorGUILayout.FloatField("素材角度修正（度）", thisConfig.BillboardAngleOffset);
            TimelineEditorUtil.DrawStableHelp("作用于独立视觉子节点；朝右素材填 0，朝上填 -90。无需在 Prefab 上挂业务脚本。");
        }

        EnsureNodeData();
        control.PreviewBindings.DrawBinding(config, true);
        ResolveTargetUnitMono();

        TimelineEditorUtil.PointDataInspector(
            this.control,
            "起点 (StartData)",
            thisConfig.StartData,
            out bool isBigChange,
            out bool isSmallChange,
            showUseOriginalOptions: true,
            showDontFollowOption: true,
            nodeHost: sourceUnitMono);
        DrawInitialScaleField();
        TimelineEditorUtil.PointDataInspector(
            this.control,
            "终点 (EndData)",
            thisConfig.EndData,
            out bool isBigChange2,
            out bool isSmallChange2,
            nodeHost: targetUnitMonos.Count > 0 ? targetUnitMonos[0] : null);
        if (EditorGUI.EndChangeCheck())
        {
            // 只更新时长；预览实例重建必须 delayCall，禁止在 IMGUI 中途 DestroyImmediate（会控件 ID 错乱/点击错位）
            RefreshFlyDuration();
            ScheduleBulletPreviewRefresh();
        }

        EditorGUI.BeginDisabledGroup(true);
        DrawFlyDurationPreview();
        EditorGUI.EndDisabledGroup();
    }

    private bool pendingBulletPreviewRefresh;

    /// <summary>延后到本帧 IMGUI 结束后再重建子弹预览，避免属性面板控件错位。</summary>
    private void ScheduleBulletPreviewRefresh()
    {
        if (pendingBulletPreviewRefresh)
        {
            return;
        }

        pendingBulletPreviewRefresh = true;
        EditorApplication.delayCall += OnDeferredBulletPreviewRefresh;
    }

    private void OnDeferredBulletPreviewRefresh()
    {
        pendingBulletPreviewRefresh = false;
        if (thisConfig == null || control == null)
        {
            return;
        }

        if (bulletInstances.Count > 0)
        {
            CreateBulletInstance();
        }

        control.Timeline.Repaint();
        SceneView.RepaintAll();
    }

    private void DrawTrajectoryTypeParams()
    {
        var isParabola = thisConfig.TrajectoryType == TimelineBulletTrajectoryType.Parabola;
        // 始终画同一套控件，避免切换弹道类型时下方控件 ID 错位
        EditorGUI.BeginDisabledGroup(!isParabola);
        thisConfig.ArcHeightFactor = EditorGUILayout.FloatField(
            "弧高系数",
            thisConfig.ArcHeightFactor);
        EditorGUI.EndDisabledGroup();
        TimelineEditorUtil.DrawStableHelp(
            isParabola
                ? "弧高：弦中点上抬 = 距离 × 系数（默认 0.35）。"
                : "直线：起飞时锁定终点，目标移动不改轨迹。");
    }

    /// <summary>
    /// 延迟缩放关键帧列表：勾选重置缩放且延迟&gt;0 时可编辑；始终画一块区域避免控件错位。
    /// 每项四框：X Y Z + 本段时长；最后接到起点「缩放」。
    /// </summary>
    private void DrawInitialScaleField()
    {
        thisConfig.DelayScaleKeys ??= new List<TimelineBulletDelayScaleKey>();
        var enabled = TimelineClipPlayBulletUtil.NeedsDelayScaleLerp(thisConfig);
        EditorGUILayout.LabelField("延迟缩放关键帧", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(!enabled);

        var keys = thisConfig.DelayScaleKeys;
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (key == null)
            {
                key = new TimelineBulletDelayScaleKey();
                keys[i] = key;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"#{i + 1}", GUILayout.Width(28));
            var scale = key.Scale;
            scale.x = EditorGUILayout.FloatField(scale.x);
            scale.y = EditorGUILayout.FloatField(scale.y);
            scale.z = EditorGUILayout.FloatField(scale.z);
            key.Scale = scale;
            key.Duration = Mathf.Max(0f, EditorGUILayout.FloatField(key.Duration));
            if (GUILayout.Button("删", GUILayout.Width(32)))
            {
                keys.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                break;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("XYZ / 本段时长(秒)", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("添加关键帧", GUILayout.Width(80)))
        {
            var lastScale = keys.Count > 0 ? keys[keys.Count - 1].Scale : Vector3.zero;
            keys.Add(new TimelineBulletDelayScaleKey
            {
                Scale = lastScale,
                Duration = 0.5f,
            });
        }

        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
        TimelineEditorUtil.DrawStableHelp(
            enabled
                ? "延迟段：按关键帧顺序从本项缩放插值到下一项（末项接到起点「缩放」）。时长为本段持续秒。"
                : "需勾选起点「重置缩放」且延迟移动时间 > 0 才可编辑延迟缩放关键帧。");
    }

    public override void OnSceneGUI(SceneView sceneView)
    {
        base.OnSceneGUI(sceneView);
        ResolveSourceUnitMono();
        EnsureNodeData();
        if (sourceUnitMono == null || thisConfig.StartData == null)
        {
            return;
        }

        DrawStartToEndGuide();

        if (!TimelineEditorUtil.DrawPointDataSceneHandles(
                thisConfig.StartData,
                sourceUnitMono,
                "子弹起点"))
        {
            return;
        }

        AfterStartHandleChanged();
    }

    /// <summary>起点到首个目标终点的参考线（不可拖终点）。</summary>
    private void DrawStartToEndGuide()
    {
        ResolveTargetUnitMono();
        TimelineTransformUtil.GetTransformationFromUnitNodePointData(
            sourceUnitMono.transform.position,
            sourceUnitMono,
            thisConfig.StartData,
            out var startPos,
            out _,
            out _);

        if (targetUnitMonos.Count == 0)
        {
            return;
        }

        TimelineTransformUtil.GetTransformationFromUnitNodePointData(
            sourceUnitMono.transform.position,
            targetUnitMonos[0],
            thisConfig.EndData,
            out var endPos,
            out _,
            out _);

        var endSize = HandleUtility.GetHandleSize(endPos) * 0.1f;
        Handles.color = new Color(1f, 0.45f, 0.2f, 0.9f);
        Handles.DrawDottedLine(startPos, endPos, 4f);
        Handles.SphereHandleCap(0, endPos, Quaternion.identity, endSize, EventType.Repaint);
        Handles.Label(endPos + Vector3.up * HandleUtility.GetHandleSize(endPos) * 0.22f, "子弹终点(参考)");
    }

    private void AfterStartHandleChanged()
    {
        RefreshFlyDuration();
        ScheduleBulletPreviewRefresh();
    }

    private void DrawFlyDurationPreview()
    {
        ResolveSourceUnitMono();
        ResolveTargetUnitMono();

        if (sourceUnitMono == null || targetUnitMonos.Count == 0)
        {
            return;
        }

        var maxDuration = 0f;
        var maxFly = 0f;
        for (var i = 0; i < targetUnitMonos.Count; i++)
        {
            var fly = BulletEntity.ComputeFlyDuration(
                sourceUnitMono,
                thisConfig,
                targetUnitMonos[i]);
            var total = BulletEntity.ComputeTotalDuration(
                sourceUnitMono,
                thisConfig,
                targetUnitMonos[i]);
            maxFly = Mathf.Max(maxFly, fly);
            maxDuration = Mathf.Max(maxDuration, total);
        }

        EditorGUILayout.FloatField("飞行时长", maxFly);
        EditorGUILayout.FloatField("总时长(含延迟)", maxDuration);
    }

    public override void OnDestroy()
    {
        DestroyBulletInstances();
        base.OnDestroy();
    }

    protected override void OnOutTime()
    {
        for (int i = 0; i < this.targetUnitMonos.Count; i++)
        {
            FinishCallback(this.targetUnitMonos[i]);
        }
        base.OnOutTime();
    }

    protected override void OnInTime()
    {
        if (prefab == null && !string.IsNullOrEmpty(prefabPath))
        {
            prefab = EditorTimelineAssetPathUtil.LoadEffectPrefab(prefabPath);
        }

        ResolveSourceUnitMono();
        ResolveTargetUnitMono();
        CreateBulletInstance();
    }

    protected override void OnInTimeLogic()
    {
        base.OnInTimeLogic();
        var sampleTiming = Mathf.Max(playTiming, 0f);
        for (int i = 0; i < bulletEntitys.Count; i++)
        {
            bulletInstances[i].SetActive(true);
            if (i < bulletParticleRoots.Count)
            {
                EditorTimelineUtil.SimulateEffectParticles(bulletParticleRoots[i], sampleTiming);
            }
            bulletEntitys[i].Tick(playTiming);
        }
    }

    private void ApplyBulletPath(string newPath)
    {
        var logical = EditorTimelineAssetPathUtil.ToEffectLogicalName(newPath);
        thisConfig.Path = logical;
        prefabPath = logical;
        prefab = EditorTimelineAssetPathUtil.LoadEffectPrefab(logical);
        ResolveTargetUnitMono();
        CreateBulletInstance();
        ChangeClipTimeStateType(ClipTimeStateType.OutTime);
        control.Timeline.StopPreView();
    }

    public override void OnPreviewQualityChanged()
    {
        base.OnPreviewQualityChanged();
        DestroyBulletInstances();
    }

    private void CreateBulletInstance()
    {
        DestroyBulletInstances();
        if (prefab == null || targetUnitMonos.Count == 0)
        {
            return;
        }

        flyDurations = Enumerable.Repeat(0f, targetUnitMonos.Count).ToList();
        bulletEntitys.Clear();
        bulletParticleRoots.Clear();
        for (int i = 0; i < targetUnitMonos.Count; i++)
        {
            var bulletInstance = Object.Instantiate(prefab);
            bulletInstances.Add(bulletInstance);

            List<ParticleSystem> systems = null;
            List<ParticleSystem> roots = null;
            EditorTimelineUtil.SetupEffectParticlePreview(bulletInstance, ref systems, ref roots);
            bulletParticleRoots.Add(roots);

            var bulletEntity = new BulletEntity();
            bulletEntity.Init(sourceUnitMono, targetUnitMonos[i], bulletInstance.transform, thisConfig, FinishCallback);
            bulletEntitys.Add(bulletEntity);

            flyDurations[i] = bulletEntity.TotalDuration;
        }
    }

    private void FinishCallback(UnitMono target)
    {
        var count = targetUnitMonos.Count;
        if (bulletInstances.Count < count)
        {
            count = bulletInstances.Count;
        }

        for (var i = count - 1; i >= 0; i--)
        {
            if (targetUnitMonos[i].Equals(target) && bulletInstances[i].activeSelf)
            {
                control.TargetControl.AddTarget(config.ClipKey, target);
                bulletInstances[i].SetActive(false);
                break;
            }
        }
    }

    private void DestroyBulletInstances()
    {
        for (int i = 0; i < bulletInstances.Count; i++)
        {
            var bulletInstance = bulletInstances[i];
            bulletInstance.transform.DOKill();
            Object.DestroyImmediate(bulletInstance);
        }
        bulletInstances.Clear();
        bulletParticleRoots.Clear();
        bulletEntitys.Clear();
        flyDurations.Clear();
    }

    /// <summary>
    /// 按片段绑定的效果来源和目标重算飞行时长，与共享播放器使用同一组目标。
    /// </summary>
    private void RefreshFlyDuration()
    {
        ResolveSourceUnitMono();
        ResolveTargetUnitMono();

        if (flyDurations.Count != targetUnitMonos.Count)
        {
            flyDurations.Clear();
            for (var i = 0; i < targetUnitMonos.Count; i++)
            {
                flyDurations.Add(0f);
            }
        }

        for (var i = 0; i < targetUnitMonos.Count; i++)
        {
            flyDurations[i] = BulletEntity.ComputeTotalDuration(sourceUnitMono, thisConfig, targetUnitMonos[i]);
        }
    }

    private void EnsureNodeData()
    {
        thisConfig.StartData ??= new TimelineClipPointData
        {
            OffsetNodeType = UnitNodeType.Root,
            OffsetScale = Vector3.one,
            OverlayCharacterFacing = true,
        };
        thisConfig.EndData ??= new TimelineClipPointData
        {
            OffsetNodeType = UnitNodeType.Spine,
            OffsetScale = Vector3.one,
        };
        thisConfig.DelayScaleKeys ??= new List<TimelineBulletDelayScaleKey>();
    }



    private void ResolveSourceUnitMono()
    {
        sourceUnitMono = control.PreviewBindings.GetSource(config);
    }

    private void ResolveTargetUnitMono()
    {
        ResolveSourceUnitMono();
        targetUnitMonos = control.UnitBindings.ResolveUnits(config, TimelineUnitSubjectType.EffectTargets, config.TargetBindingKey);
        if (!sourceUnitMono)
        {
            targetUnitMonos.Clear();
        }
    }
}
