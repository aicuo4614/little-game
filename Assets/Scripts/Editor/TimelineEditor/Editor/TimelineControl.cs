using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

public class TimelineControl
{
    private List<TimelineClipBase> clips = new List<TimelineClipBase>();
    public List<TimelineClipBase> Clips => clips;

    private EditorTimeLine timeline;
    public EditorTimeLine Timeline => timeline;

    private TimelineConfig config;
    public TimelineConfig Config => config;

    private TimelineMenuControl menuControl;
    public TimelineMenuControl MenuControl => menuControl;

    private TimelineEditorUnitsControl unitsControl;
    public TimelineEditorUnitsControl UnitsControl => unitsControl;

    private TimelineEditorShortcutControl shortcutControl;
    public TimelineEditorShortcutControl ShortcutControl => shortcutControl;

    private TimelineEditorEnemyControl enemyControl;
    public TimelineEditorEnemyControl EnemyControl => enemyControl;

    private TimelinePreviewRefreshControl previewRefresh;
    /// <summary>保留当前播放头的参数刷新入口。</summary>
    public TimelinePreviewRefreshControl PreviewRefresh => previewRefresh;

    private TimelinePreviewBindingControl previewBindings;
    /// <summary>编辑器预览角色和目标绑定。</summary>
    public TimelinePreviewBindingControl PreviewBindings => previewBindings;
    private TimelineUnitBindingControl unitBindings;
    /// <summary>单位片段共用的对象绑定和表现适配。</summary>
    public TimelineUnitBindingControl UnitBindings => unitBindings;
    private TimelinePreviewPlayback sharedPlayback;
    /// <summary>预览宿主，执行核心位于 shared。</summary>
    public TimelinePreviewPlayback SharedPlayback => sharedPlayback;

    private TimelineTargetControl targetControl;
    public TimelineTargetControl TargetControl => targetControl;

    public void Init(EditorTimeLine timeline, string json)
    {
        var config = TimelineJsonUtil.FromJson(json);
        Init(timeline, config);
    }

    /// <summary>初始化技能预览，按施法阵营创建双方单位。</summary>
    public void Init(EditorTimeLine timeline, TimelineConfig config, UnitTeamType casterTeam = UnitTeamType.TeamA,
        bool resetSceneView = true)
    {
        this.timeline = timeline;
        this.config = config;
        previewCasterTeam = casterTeam;
        menuControl = new TimelineMenuControl(this);
        unitsControl = new TimelineEditorUnitsControl(this);
        shortcutControl = new TimelineEditorShortcutControl(this);
        enemyControl = new TimelineEditorEnemyControl(this);
        targetControl = new TimelineTargetControl(this);
        previewBindings = new TimelinePreviewBindingControl(this);
        unitBindings = new TimelineUnitBindingControl(this);
        sharedPlayback = new TimelinePreviewPlayback(this);
        previewRefresh = new TimelinePreviewRefreshControl(this);

        this.config = config;

        SetOriginCamera(resetSceneView);

        // 新建、重载和 MCP 应用草稿都会重建 Control；须先恢复角色再解析其专属动作。
        SetMyTransform(config.PreviewCasterAddress);

        for (int i = 0; i < this.config.Clips.Count; i++)
        {
            var clipConfig = this.config.Clips[i];
            if (clipConfig == null)
            {
                Debug.LogWarning($"[TimelineControl] Clips[{i}] 解析为 null，已跳过。");
                continue;
            }

            var clip = CreateClip(clipConfig);
            if (clip == null)
            {
                Debug.LogWarning($"[TimelineControl] 不支持的 Clip 类型: {clipConfig.Type}，已跳过。");
                continue;
            }

            clip.PreInit(this, clipConfig);
            clips.Add(clip);
        }

        for (int i = 0; i < clips.Count; i++)
        {
            clips[i].Init(this, clips[i].Config);
        }
    }

    public void OnDrawGizmos()
    {
        for (int i = 0; i < clips.Count; i++)
        {
            if (clips[i].CurClipOperateStateType == TimelineClipBase.ClipOperateStateType.Selected)
            {
                clips[i].OnDrawGizmos();    
            }
        }
    }

    /// <summary>转发 Scene 视图交互到当前选中的 Clip。</summary>
    public void OnSceneGUI(SceneView sceneView)
    {
        for (var i = 0; i < clips.Count; i++)
        {
            if (clips[i].CurClipOperateStateType == TimelineClipBase.ClipOperateStateType.Selected)
            {
                clips[i].OnSceneGUI(sceneView);
            }
        }
    }

    public void OnEditorUpdate(float deltaTime)
    {
        unitsControl.UpdateSpineFacing();
        sharedPlayback.Evaluate();
        for (int i = 0; i < clips.Count; i++)
        {
            if (!TimelinePlayback.Supports(clips[i].Config))
            {
                clips[i].EditorUpdateLogic(deltaTime);
            }
        }
    }

    private void SetOriginCamera(bool resetSceneView)
    {
        if (resetSceneView && SceneView.lastActiveSceneView != null)
        {
            TimelinePortraitPreview.FrameSceneView();
        }

        var battleCameraGo = EditorTimelineUtil.FindBattleCameraGameObject();
        if (battleCameraGo == null)
        {
            Debug.LogWarning("[TimelineControl] 场景中未找到 BattleCamera，跳过相机复位。");
            return;
        }

        var battleCamera = BattleCameraMonoBinder.EnsureBound(battleCameraGo);
        TimelinePortraitPreview.ApplyCamera(battleCamera);
    }


    #region myTransform
    private Transform myTransform;
    public Transform MyTransform => myTransform;
    private GameObject myTransformPrefab;
    private string myTransformPath;
    public string MyTransformPath => myTransformPath;

    /// <summary>
    /// 编辑器预览施法阵营（不写 JSON）。默认 TeamA；切 TeamB 可验证「阵营镜像」相机。
    /// </summary>
    private UnitTeamType previewCasterTeam = UnitTeamType.TeamA;
    public UnitTeamType PreviewCasterTeam => previewCasterTeam;

    private Vector3 myTransformOriginPos => TimelinePortraitPreview.Position(this, previewCasterTeam, 0);
    public Vector3 MyTransformOriginPos => myTransformOriginPos;

    private Quaternion myTransformOriginRotate =>
        Quaternion.Euler(previewCasterTeam == UnitTeamType.TeamB ? new Vector3(0, -90, 0) : new Vector3(0, 90, 0));
    public Quaternion MyTransformOriginRotate => myTransformOriginRotate;

    /// <summary>对面预览单位阵营：施法 TeamA → 对面 TeamB，反之亦然。</summary>
    public UnitTeamType PreviewOpponentTeam =>
        previewCasterTeam == UnitTeamType.TeamB ? UnitTeamType.TeamA : UnitTeamType.TeamB;

    public Transform SetMyTransform(string path)
    {
        return SetMyTransform(path, false);
    }

    /// <param name="forceRecreate">路径未变也重建（换预览阵营时用）。</param>
    public Transform SetMyTransform(string path, bool forceRecreate)
    {
        // JSON / 调用方传逻辑名；加载按 PreviewQuality（HQ→Low）解析工程路径
        var logical = EditorTimelineAssetPathUtil.ToUnitLogicalName(path);
        if (string.IsNullOrEmpty(logical))
        {
            logical = EditorTimelineAssetPathUtil.DefaultUnitLogicalName;
        }

        if (!forceRecreate && myTransformPath == logical && this.myTransform != null)
        {
            return this.myTransform;
        }

        myTransformPrefab = EditorTimelineAssetPathUtil.LoadUnitPrefab(logical);

        if (this.myTransform != null)
        {
            unitsControl.RemoveTransform(this.myTransform);
            GameObject.DestroyImmediate(this.myTransform.gameObject);
            this.myTransform = null;
        }

        var unit = this.unitsControl.CreateUnit(
            previewCasterTeam,
            logical,
            myTransformOriginPos,
            myTransformOriginRotate,
            Vector3.one);
        if (unit == null)
        {
            throw new System.InvalidOperationException(
                $"无法创建施法者单位，请检查逻辑名/画质资源: {logical}（预览画质 {EditorTimelineAssetPathUtil.PreviewQuality}）");
        }

        this.myTransform = unit.Transform;
        for (int i = 0; i < this.clips.Count; i++)
        {
            this.clips[i].OnSetMyTransformPrefabPath(logical);
        }

        this.myTransformPath = logical;
        return this.myTransform;
    }

    /// <summary>设置并保存当前文档的预览主角；空值恢复默认，暂停并保留播放头。</summary>
    public void SetPreviewCaster(string address)
    {
        if (timeline.IsRecording || timeline.IsPlayheadLerping)
        {
            throw new System.InvalidOperationException(TimelineBindingEditorText.PreviewCasterBusy);
        }
        var logical = EditorTimelineAssetPathUtil.ToUnitLogicalName(address);
        var draft = CaptureDocument();
        draft.PreviewCasterAddress = string.IsNullOrEmpty(logical) ? null : logical;
        TimelineEditorMcpValidation.ValidatePreviewCaster(draft);
        TimelineEditorMcpValidation.ResolveSpineDurations(draft, logical);
        var previous = config.PreviewCasterAddress;
        var time = timeline.RunningTime;
        timeline.PausePreView();
        OnStopPreview(false);
        try
        {
            SetMyTransform(logical);
            config.PreviewCasterAddress = draft.PreviewCasterAddress;
        }
        catch
        {
            SetMyTransform(previous, true);
            throw;
        }
        finally
        {
            sharedPlayback.Invalidate();
            timeline.SeekRunningTimeImmediate(time);
            previewRefresh.Request();
            timeline.RefreshUnsavedChangesFlag();
            timeline.Repaint();
        }
    }

    private string previewCasterError;

    private void DrawPreviewCaster()
    {
        using (new EditorGUI.DisabledScope(timeline.IsRecording || timeline.IsPlayheadLerping))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var next = (GameObject)EditorGUILayout.ObjectField(TimelineBindingEditorText.PreviewCaster,
                myTransformPrefab, typeof(GameObject), false);
            var changed = EditorGUI.EndChangeCheck();
            var reset = GUILayout.Button(TimelineBindingEditorText.ResetPreviewCaster, GUILayout.Width(72));
            EditorGUILayout.EndHorizontal();
            if (changed || reset)
            {
                try
                {
                    var assetPath = next ? AssetDatabase.GetAssetPath(next) : null;
                    if (!reset && next && (!assetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)
                        || !assetPath.StartsWith("Assets/Package_Unit/", System.StringComparison.Ordinal)))
                    {
                        throw new System.ArgumentException(TimelineBindingEditorText.InvalidPreviewCaster);
                    }
                    SetPreviewCaster(reset ? null : assetPath);
                    previewCasterError = null;
                }
                catch (System.Exception exception)
                {
                    previewCasterError = exception.Message;
                }
            }
        }
        EditorGUILayout.HelpBox(TimelineBindingEditorText.PreviewCasterHelp, MessageType.Info);
        if (!string.IsNullOrEmpty(previewCasterError))
        {
            EditorGUILayout.HelpBox(previewCasterError, MessageType.Warning);
        }
    }

    /// <summary>切换预览画质 Low/HQ（不写 JSON）；对齐运行时 HQ 缺则回落 Low。</summary>
    public void SetPreviewQuality(TimelinePreviewQualityType quality)
    {
        if (EditorTimelineAssetPathUtil.PreviewQuality == quality && myTransform != null)
        {
            return;
        }

        timeline?.StopPreView();
        EditorTimelineAssetPathUtil.PreviewQuality = quality;

        var path = myTransformPath;
        if (string.IsNullOrEmpty(path))
        {
            path = EditorTimelineAssetPathUtil.DefaultUnitLogicalName;
        }

        SetMyTransform(path, true);
        enemyControl?.RebuildOpponents(PreviewOpponentTeam);
        for (var i = 0; i < clips.Count; i++)
        {
            clips[i].OnPreviewQualityChanged();
        }

        timeline?.Repaint();
        SceneView.RepaintAll();
    }

    /// <summary>左侧：预览画质 Low / HQ。</summary>
    public void DrawPreviewQualityBar()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("预览画质", GUILayout.Width(88));
        var index = EditorTimelineAssetPathUtil.PreviewQuality == TimelinePreviewQualityType.HQ ? 1 : 0;
        var nextIndex = GUILayout.Toolbar(index, new[] { "Low", "HQ" });
        EditorGUILayout.EndHorizontal();

        var nextQuality = nextIndex == 1 ? TimelinePreviewQualityType.HQ : TimelinePreviewQualityType.Low;
        if (nextQuality != EditorTimelineAssetPathUtil.PreviewQuality)
        {
            SetPreviewQuality(nextQuality);
        }

        if (EditorTimelineAssetPathUtil.PreviewQuality == TimelinePreviewQualityType.HQ)
        {
            EditorGUILayout.HelpBox(
                "HQ 档：单位/特效优先加载 HQ；找不到则回落 Low（与运行时 UnitLoadMgr / EffectMgr 一致）。",
                MessageType.Info);
        }
    }

    /// <summary>
    /// 切换预览施法阵营：重建施法者站位/朝向，对面单位换到另一侧。不写入技能 JSON。
    /// </summary>
    public void SetPreviewCasterTeam(UnitTeamType team)
    {
        if (team != UnitTeamType.TeamA && team != UnitTeamType.TeamB)
        {
            team = UnitTeamType.TeamA;
        }

        if (previewCasterTeam == team && myTransform != null)
        {
            return;
        }

        timeline?.StopPreView();
        previewCasterTeam = team;
        enemyControl?.RebuildOpponents(PreviewOpponentTeam);

        var path = myTransformPath;
        if (string.IsNullOrEmpty(path))
        {
            path = EditorTimelineAssetPathUtil.DefaultUnitLogicalName;
        }

        SetMyTransform(path, true);
        timeline?.Repaint();
        SceneView.RepaintAll();
    }

    /// <summary>左侧：预览施法阵营 TeamA / TeamB 切换。</summary>
    public void DrawPreviewCasterTeamBar()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("预览施法阵营", GUILayout.Width(88));
        var index = previewCasterTeam == UnitTeamType.TeamB ? 1 : 0;
        var nextIndex = GUILayout.Toolbar(index, new[] { "TeamA(左)", "TeamB(右)" });
        EditorGUILayout.EndHorizontal();

        var nextTeam = nextIndex == 1 ? UnitTeamType.TeamB : UnitTeamType.TeamA;
        if (nextTeam != previewCasterTeam)
        {
            SetPreviewCasterTeam(nextTeam);
        }

        if (previewCasterTeam == UnitTeamType.TeamB)
        {
            EditorGUILayout.HelpBox(
                "当前按 TeamB 第 0 站位预览；勾选「阵营镜像」的相机 Move / 运镜会走镜像逻辑。",
                MessageType.Info);
        }
    }

    #endregion


    private TimelineClipBase selectClip;
    public TimelineClipBase SelectClip => selectClip;

    private TimelineEventBase selectEvent;
    public TimelineEventBase SelectEvent => selectEvent;

    private TimelineNodeBase selectNode;
    public TimelineNodeBase SelectNode => selectNode;

    private TimelineClipBase pendingEndByOtherClipCatchOwner;
    private string endByOtherClipCatchFeedback;
    private TimelineClipCameraShotBlend pendingCameraShotBlendCatchOwner;

    public bool IsWaitingEndByOtherClipCatch(TimelineClipBase clip)
    {
        return pendingEndByOtherClipCatchOwner == clip;
    }

    public void BeginEndByOtherClipCatch(TimelineClipBase owner)
    {
        pendingEndByOtherClipCatchOwner = owner;
        pendingCameraShotBlendCatchOwner = null;
        endByOtherClipCatchFeedback = null;
        timeline?.Repaint();
    }

    public void TryCompleteEndByOtherClipCatch(TimelineClipBase selectedClip)
    {
        if (pendingEndByOtherClipCatchOwner == null || selectedClip == null)
        {
            return;
        }

        if (selectedClip == pendingEndByOtherClipCatchOwner || selectedClip.Config == null)
        {
            return;
        }

        var owner = pendingEndByOtherClipCatchOwner;
        owner.Config.EndByOtherClipEnd = selectedClip.Config.ClipKey;
        SyncClipDurationFromCatch(owner.Config);
        pendingEndByOtherClipCatchOwner = null;
        endByOtherClipCatchFeedback =
            $"Catch 成功：{UnitFeatureTypeDisplay.GetClipLabelName(owner.Config)} 已绑定 {UnitFeatureTypeDisplay.GetClipLabelName(selectedClip.Config)}";
        timeline?.Repaint();
    }

    public bool IsWaitingCameraShotBlendCatch(TimelineClipBase clip)
    {
        return pendingCameraShotBlendCatchOwner == clip;
    }

    public void BeginCameraShotBlendCatch(TimelineClipCameraShotBlend owner)
    {
        pendingCameraShotBlendCatchOwner = owner;
        pendingEndByOtherClipCatchOwner = null;
        endByOtherClipCatchFeedback = null;
        timeline?.Repaint();
    }

    public void TryCompleteCameraShotBlendCatch(TimelineClipBase selectedClip)
    {
        if (pendingCameraShotBlendCatchOwner == null || selectedClip == null)
        {
            return;
        }

        if (selectedClip == pendingCameraShotBlendCatchOwner || selectedClip.Config == null)
        {
            return;
        }

        if (selectedClip.Config.Type != TimelineClipType.CameraShot)
        {
            endByOtherClipCatchFeedback = "Catch 失败：请选择「相机运镜」Clip";
            timeline?.Repaint();
            return;
        }

        var owner = pendingCameraShotBlendCatchOwner;
        var blendConfig = owner.Config as TimelineClipCameraShotBlendConfig;
        blendConfig.NextCameraShotClipKey = selectedClip.Config.ClipKey;
        pendingCameraShotBlendCatchOwner = null;
        endByOtherClipCatchFeedback =
            $"Catch 成功：运镜过渡已绑定 {UnitFeatureTypeDisplay.GetClipLabelName(selectedClip.Config)}";
        timeline?.Repaint();
    }

    private bool selectNothingInDrawRect;
    public bool SelectNothingInDrawRect => selectNothingInDrawRect;

    public bool SelectNothingOutDrawRect => this.timeline.IsSelectNoDrawRect;

    public void Draw()
    {
        previewRefresh.Process();
        SyncAllCatchClipDurations();

        // 先刷敌人 Idle 底稿，再画 Clips；否则 PlayHit 的受击会被 Idle 每帧盖掉
        sharedPlayback.Evaluate();

        for (int i = 0; i < clips.Count; i++)
        {
            clips[i].Draw();
        }

        NodeDragConnectLogic();
        menuControl.Draw();
        targetControl.Draw();
        unitsControl.Draw();
        if (Event.current.rawType == EventType.MouseUp || Event.current.rawType == EventType.KeyUp)
        {
            sharedPlayback.Invalidate();
        }
    }

    public void DrawH()
    {
        MouseClickTypeLogic();

        TimelineClipBase clip = null;
        TimelineEventBase e = null;
        TimelineNodeBase node = null;
        bool selectInDraw = false;
        if (this.MouseClickType == MouseEventClickType.MouseLeftDown || this.MouseClickType == MouseEventClickType.MouseRightUp)
        {
            IsSelectSomething(ref clip, ref e, ref node, ref selectInDraw, this.MouseClickType);
        }
        selectClip = clip;
        selectEvent = e;
        selectNode = node;
        selectNothingInDrawRect = selectInDraw;
    }

    public void DrawInspector()
    {
        EditorGUI.BeginChangeCheck();
        DrawInspectorFields();
        if (EditorGUI.EndChangeCheck())
        {
            sharedPlayback.Invalidate();
            timeline?.RefreshUnsavedChangesFlag();
        }
    }

    private void DrawInspectorFields()
    {
        if (!string.IsNullOrEmpty(endByOtherClipCatchFeedback))
        {
            EditorGUILayout.HelpBox(endByOtherClipCatchFeedback, MessageType.Info);
        }

        if (config != null)
        {
            EditorGUILayout.LabelField("Timeline", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var unsavedMark = timeline.hasUnsavedChanges ? "*" : " ";
            EditorGUILayout.LabelField(unsavedMark, GUILayout.Width(12));
            var nextTimelineId = EditorGUILayout.TextField("TimelineId", config.TimelineId ?? string.Empty);
            if (nextTimelineId != (config.TimelineId ?? string.Empty))
            {
                config.TimelineId = nextTimelineId;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("备注");
            var nextDescription = EditorGUILayout.TextArea(
                config.Description ?? string.Empty,
                EditorStyles.textArea,
                GUILayout.MinHeight(48));
            if (nextDescription != (config.Description ?? string.Empty))
            {
                config.Description = string.IsNullOrEmpty(nextDescription) ? null : nextDescription;
            }

            DrawPreviewCaster();

            var selectedClip = GetSelectedClip();
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("DurationBindClipKey", config.DurationBindClipKey ?? string.Empty);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(selectedClip == null);
            if (GUILayout.Button("抓取End", GUILayout.Width(64)))
            {
                if (selectedClip.Config != null)
                {
                    config.DurationBindClipKey = selectedClip.Config.ClipKey;
                }
                timeline?.Repaint();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.FloatField("Duration", GetBoundClipEndTime(config));
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4);
        }

        if (clips != null)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i].CurClipOperateStateType == TimelineClipBase.ClipOperateStateType.Selected
                    || clips[i].CurClipOperateStateType == TimelineClipBase.ClipOperateStateType.Drag)
                {
                    clips[i].DrawInspector();
                }

                var allEvents = clips[i].GetAllEvents();
                for (int j = 0; j < allEvents.Count; j++) 
                {
                    if (allEvents[j].CurClipOperateStateType == TimelineEventBase.ClipOperateStateType.Selected
                        || allEvents[j].CurClipOperateStateType == TimelineEventBase.ClipOperateStateType.Drag
                        )
                    {
                        allEvents[j].DrawInspector();
                    }
                }
            }
        }
    }

    public TimelineClipBase GetSelectedClip()
    {
        if (clips == null)
        {
            return null;
        }

        for (int i = 0; i < clips.Count; i++)
        {
            var state = clips[i].CurClipOperateStateType;
            if (state == TimelineClipBase.ClipOperateStateType.Selected
                || state == TimelineClipBase.ClipOperateStateType.Drag)
            {
                return clips[i];
            }
        }

        return null;
    }

    /// <summary>读取持久选中状态，点击命中字段会在每次重绘时重置。</summary>
    internal TimelineEventBase GetSelectedEvent()
    {
        return clips.SelectMany(clip => clip.GetAllEvents()).FirstOrDefault(point =>
            point.CurClipOperateStateType is TimelineEventBase.ClipOperateStateType.Selected
                or TimelineEventBase.ClipOperateStateType.Drag);
    }

    /// <summary>读取当前选中的连线端点。</summary>
    internal TimelineNodeBase GetSelectedNode()
    {
        return clips.SelectMany(clip => clip.Nodes).FirstOrDefault(node =>
            node.CurNodeOperateStateType is TimelineNodeBase.NodeOperateStateType.Selected
                or TimelineNodeBase.NodeOperateStateType.Drag);
    }

    #region Clip 显示名搜索

    private string clipSearchKeyword = string.Empty;
    private List<TimelineClipBase> clipSearchMatches = new List<TimelineClipBase>();
    private int clipSearchMatchIndex = -1;

    /// <summary>
    /// 左侧播放栏下方、属性上方：按 Clip 条显示名包含匹配；右侧「下一个」循环选中。
    /// </summary>
    public void DrawClipSearchBar()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        var nextKeyword = EditorGUILayout.TextField(
            clipSearchKeyword,
            EditorStyles.toolbarSearchField);
        if (EditorGUI.EndChangeCheck())
        {
            ApplyClipSearchKeyword(nextKeyword);
        }

        EditorGUI.BeginDisabledGroup(clipSearchMatches.Count == 0);
        if (GUILayout.Button("下一个", GUILayout.Width(52)))
        {
            SelectNextClipSearchMatch();
        }

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(clipSearchKeyword))
        {
            EditorGUILayout.LabelField(
                string.Format("匹配 {0} 条", clipSearchMatches.Count),
                EditorStyles.miniLabel);
        }
    }

    /// <summary>关键字变化：重建匹配列表并选中第一条；空关键字不改选中。</summary>
    private void ApplyClipSearchKeyword(string keyword)
    {
        clipSearchKeyword = keyword ?? string.Empty;
        RebuildClipSearchMatches();
        if (clipSearchMatches.Count == 0)
        {
            clipSearchMatchIndex = -1;
            timeline?.Repaint();
            return;
        }

        clipSearchMatchIndex = 0;
        SelectClipProgrammatically(clipSearchMatches[0]);
        timeline?.Repaint();
    }

    /// <summary>在当前匹配结果中选中下一条（到末尾回绕）。</summary>
    public void SelectNextClipSearchMatch()
    {
        RebuildClipSearchMatches();
        if (clipSearchMatches.Count == 0)
        {
            clipSearchMatchIndex = -1;
            return;
        }

        clipSearchMatchIndex = (clipSearchMatchIndex + 1) % clipSearchMatches.Count;
        SelectClipProgrammatically(clipSearchMatches[clipSearchMatchIndex]);
        timeline?.Repaint();
    }

    private void RebuildClipSearchMatches()
    {
        clipSearchMatches.Clear();
        if (clips == null || string.IsNullOrWhiteSpace(clipSearchKeyword))
        {
            return;
        }

        var key = clipSearchKeyword.Trim();
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            if (clip == null || clip.Config == null)
            {
                continue;
            }

            var label = UnitFeatureTypeDisplay.GetClipLabelName(clip.Config);
            if (label.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            clipSearchMatches.Add(clip);
        }

        clipSearchMatches.Sort(CompareClipSearchMatchOrder);
    }

    private static int CompareClipSearchMatchOrder(TimelineClipBase a, TimelineClipBase b)
    {
        var aStart = a.Config.StartTime + a.Config.DelayTime;
        var bStart = b.Config.StartTime + b.Config.DelayTime;
        var cmp = aStart.CompareTo(bStart);
        if (cmp != 0)
        {
            return cmp;
        }

        return string.CompareOrdinal(a.Config.ClipKey, b.Config.ClipKey);
    }

    /// <summary>程序选中 Clip：清其它 Clip/Event 选中，供搜索跳转使用。</summary>
    public void SelectClipProgrammatically(TimelineClipBase target)
    {
        if (target == null)
        {
            return;
        }

        selectClip = target;
        selectEvent = null;
        selectNode = null;

        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            if (clip == target)
            {
                clip.ChangeClipOperateStateType(TimelineClipBase.ClipOperateStateType.Selected);
            }
            else
            {
                var state = clip.CurClipOperateStateType;
                if (state == TimelineClipBase.ClipOperateStateType.Selected
                    || state == TimelineClipBase.ClipOperateStateType.Drag)
                {
                    clip.ChangeClipOperateStateType(TimelineClipBase.ClipOperateStateType.Normal);
                }
            }

            var allEvents = clip.GetAllEvents();
            for (var j = 0; j < allEvents.Count; j++)
            {
                var ev = allEvents[j];
                var evState = ev.CurClipOperateStateType;
                if (evState == TimelineEventBase.ClipOperateStateType.Selected
                    || evState == TimelineEventBase.ClipOperateStateType.Drag)
                {
                    ev.ChangeClipOperateStateType(TimelineEventBase.ClipOperateStateType.Normal);
                }
            }

            var nodes = clip.Nodes;
            for (var n = 0; n < nodes.Count; n++)
            {
                var node = nodes[n];
                if (node.CurNodeOperateStateType != TimelineNodeBase.NodeOperateStateType.Normal)
                {
                    node.ChangeNodeOperateStateType(TimelineNodeBase.NodeOperateStateType.Normal);
                }
            }
        }
    }

    #endregion

    public void OnDestroy()
    {
        sharedPlayback.Stop();
        if (this.myTransform != null)
        {
            GameObject.DestroyImmediate(this.myTransform.gameObject);
            this.myTransform = null;
        }
        if (clips != null)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                clips[i].OnDestroy();
            }
        }
        this.menuControl.OnDestroy();
        this.enemyControl.OnDestroy();
        this.targetControl?.OnDestroy();
    }

    /// <summary>复制当前画布和事件草稿，供编译恢复和 MCP 使用。</summary>
    internal TimelineConfig CaptureDocument()
    {
        // GUI 可能还没有同步 EventConfigs 或 Clip 顺序，读取以画布对象为准。
        var state = new TimelineConfig
        {
            TimelineId = config.TimelineId,
            Description = config.Description,
            PreviewCasterAddress = config.PreviewCasterAddress,
            DurationBindClipKey = config.DurationBindClipKey,
            Clips = clips.Select(clip =>
            {
                var copy = TimelineJsonUtil.FromTimelineClipConfigJson(TimelineJsonUtil.ToJson(clip.Config));
                copy.EventConfigs = clip.Events.Select(point => new TimelineEventConfigBase
                {
                    EventKey = point.Config.EventKey,
                    TimelineEventType = point.Config.TimelineEventType,
                    ValueType = point.Config.ValueType,
                    Time = point.Config.Time,
                    EventName = point.Config.EventName
                }).ToList();
                return copy;
            }).ToList()
        };
        return state;
    }

    public bool BuildConfigForSave(TimelineConfig result)
    {
        if (config == null)
        {
            result = new TimelineConfig
            {
                Clips = new List<TimelineClipConfigBase>(),
            };
            return false;
        }

        bool isSucceed = true;
        for (var i = 0; i < clips.Count; i++)
        {
            isSucceed = clips[i].SyncEventConfigsBeforeSave();
            if (!isSucceed)
            {
                result = config;
                return false;
            }
        }

        config.Clips = new List<TimelineClipConfigBase>();
        for (var i = 0; i < clips.Count; i++)
        {
            if (clips[i].Config != null)
            {
                config.Clips.Add(clips[i].Config);
            }
        }

        result = config;
        return true;
    }

    public void Reload(TimelineConfig newConfig)
    {
        unitBindings.Restore();
        selectClip = null;
        selectEvent = null;
        selectNode = null;
        pendingEndByOtherClipCatchOwner = null;
        pendingCameraShotBlendCatchOwner = null;
        endByOtherClipCatchFeedback = null;
        ClearNodeDragConnectSource();
        menuControl = new TimelineMenuControl(this);
        if (clips != null)
        {
            for (var i = 0; i < clips.Count; i++)
            {
                clips[i].OnDestroy();
            }
            clips.Clear();
        }

        config = newConfig ?? new TimelineConfig
        {
            Clips = new List<TimelineClipConfigBase>(),
        };

        if (config.Clips == null)
        {
            config.Clips = new List<TimelineClipConfigBase>();
        }

        SetMyTransform(config.PreviewCasterAddress);

        for (var i = 0; i < config.Clips.Count; i++)
        {
            var clipConfig = config.Clips[i];
            if (clipConfig == null)
            {
                continue;
            }

            var clip = CreateClip(clipConfig);
            if (clip == null)
            {
                continue;
            }

            clip.PreInit(this, clipConfig);
            clips.Add(clip);
        }

        for (var i = 0; i < clips.Count; i++)
        {
            clips[i].Init(this, clips[i].Config);
        }

        sharedPlayback.Invalidate();
        if (timeline != null)
        {
            timeline.Repaint();
        }
    }

    private TimelineClipBase CreateClip(TimelineClipConfigBase config)
    {
        var clip = CreateEmptyClip(config.Type);
        if (clip == null)
        {
            return null;
        }

        // CreateEmptyClip 会写入「新建」默认 Config（新 ClipKey、清空 PreKey）。
        // 加载/粘贴必须改回传入配置，否则 PreKey 连线全部丢失。
        clip.SetConfig(config);
        return clip;
    }

    // 创建配置默认值时不初始化预览对象，供编辑器和 MCP 共用。
    internal TimelineClipBase CreateEmptyClip(TimelineClipType type, float defaultStartTime = 0, float defaultDuration = 1)
    {
        TimelineClipBase clip = null;
        TimelineClipConfigBase config = null;
        switch (type)
        {
            case TimelineClipType.Translate:
                clip = new TimelineClipTranslate();
                config = new TimelineClipTranslateConfig()
                {
                    Type = TimelineClipType.Translate,
                    OffsetSpace = TimelineMoveOffsetSpace.ApproachDirection,
                    DurationType = DurationType.Time,
                    DurationPraram = 1f,
                    PointData = new TimelineClipPointData()
                    {
                        OffsetNodeType = UnitNodeType.Root,
                        OffsetScale = Vector3.one,
                    },
                    IsRotateToAtStart = true,
                };
                break;
            case TimelineClipType.Rotate:
                clip = new TimelineClipRotate();
                config = new TimelineClipRotateConfig()
                {
                    Type = TimelineClipType.Rotate,
                    DurationType = DurationType.Time,
                    DurationPraram = 1f,
                    StartEuler = Vector3.zero,
                    EndEuler = new Vector3(0f, 90f, 0f),
                };
                break;
            case TimelineClipType.Scale:
                clip = new TimelineClipScale();
                config = new TimelineClipScaleConfig()
                {
                    Type = TimelineClipType.Scale,
                    DurationType = DurationType.Time,
                    DurationPraram = 1f,
                    StartScale = Vector3.one,
                    EndScale = Vector3.one * 1.5f,
                };
                break;
            case TimelineClipType.PlaySpine:
            {
                clip = new TimelineClipPlaySpine();
                config = new TimelineClipPlaySpineConfig { AnimationName = "idle" };
                break;
            }
            case TimelineClipType.PlayAnimator:
                clip = new TimelineClipPlayAnimator();
                config = new TimelineClipPlayAnimatorConfig()
                {
                    Type = TimelineClipType.PlayAnimator,
                    PrefabPath = EditorTimelineAssetPathUtil.DefaultUnitLogicalName,
                    ParamName = "Attack",
                };
                break;
            case TimelineClipType.PlayBullet:
                clip = new TimelineClipPlayBullet();
                config = new TimelineClipPlayBulletConfig()
                {
                    Type = TimelineClipType.PlayBullet,
                    Path = EditorTimelineAssetPathUtil.DefaultBulletLogicalName,
                    MoveSpeed = 12f,
                    MoveDelayTime = 0f,
                    DelayScaleKeys = new List<TimelineBulletDelayScaleKey>
                    {
                        new TimelineBulletDelayScaleKey
                        {
                            Scale = Vector3.zero,
                            Duration = 0.5f,
                        },
                    },
                    TrajectoryType = TimelineBulletTrajectoryType.Parabola,
                    ArcHeightFactor = TimelineClipPlayBulletUtil.DefaultArcHeightFactor,
                    StartData = new TimelineClipPointData
                    {
                        OffsetNodeType = UnitNodeType.Root,
                        OffsetScale = Vector3.one,
                        OverlayCharacterFacing = true,
                    },
                    EndData = new TimelineClipPointData
                    {
                        OffsetNodeType = UnitNodeType.Spine,
                        OffsetScale = Vector3.one,
                    },
                };
                break;
            case TimelineClipType.PlayEffect:
                clip = new TimelineClipPlayEffect();
                config = new TimelineClipPlayEffectConfig()
                {
                    Type = TimelineClipType.PlayEffect,
                    EffectPath = EditorTimelineAssetPathUtil.DefaultEffectLogicalName,
                    AffectedByHitStop = true,
                    PointData = new TimelineClipPointData
                    {
                        OffsetNodeType = UnitNodeType.Root,
                        OffsetScale = Vector3.one,
                        NeedSetParent = true,
                        OverlayCharacterFacing = true,
                    },
                };
                break;
            case TimelineClipType.PlayHit:
                clip = new TimelineClipPlayHit();
                config = new TimelineClipPlayHitConfig()
                {
                    Type = TimelineClipType.PlayHit,
                    AnimatorParamName = "Hit",
                    EffectPath = EditorTimelineAssetPathUtil.DefaultEffectLogicalName,
                    UnitNodePointData = new TimelineClipPointData
                    {
                        OffsetNodeType = UnitNodeType.Root,
                        OffsetScale = Vector3.one,
                        NeedSetParent= true,
                        OverlayCharacterFacing = true,
                    },
                };
                break;
            case TimelineClipType.CameraShake:
                clip = new TimelineClipCameraShake();
                config = new TimelineClipCameraShakeConfig()
                {
                    Type = TimelineClipType.CameraShake,
                    Duration = 0.5f,
                };
                break;
            case TimelineClipType.CameraPathAnim:
                clip = new TimelineClipCameraPathAnim();
                config = new TimelineClipCameraPathAnimConfig()
                {
                    Type = TimelineClipType.CameraPathAnim,
                    AnimationClipPath = "Assets/Package_Unit/AnimationClip/CameraMoveTest.anim",
                    AttachNodeType = UnitNodeType.Transform,
                    Duration = 0f,
                };
                break;
            case TimelineClipType.CameraShot:
                clip = new TimelineClipCameraShot();
                config = new TimelineClipCameraShotConfig()
                {
                    Type = TimelineClipType.CameraShot,
                    ShotType = TimelineCameraShotType.PushIn,
                    Use2DComposition = true,
                    LookPivot = new TimelineCameraPivotData(),
                    FromCurrentPose = true,
                    StartDistance = 8f,
                    Distance = 4f,
                    Height = 2f,
                    MoveAmount = 2f,
                    OrbitStartYaw = -20f,
                    OrbitAngle = 50f,
                    PathEase = DG.Tweening.Ease.InOutSine,
                    MirrorByTeam = true,
                    LockCameraUpright = true,
                    Duration = 1f,
                };
                break;
            case TimelineClipType.CameraShotBlend:
                clip = new TimelineClipCameraShotBlend();
                config = new TimelineClipCameraShotBlendConfig()
                {
                    Type = TimelineClipType.CameraShotBlend,
                    NextCameraShotClipKey = "",
                    BlendEase = DG.Tweening.Ease.InOutSine,
                    BlendFov = true,
                    Duration = 0.3f,
                };
                break;
            case TimelineClipType.SetCastTargets:
            {
                clip = new TimelineClipSetCastTargets();
                config = new TimelineClipSetCastTargetsConfig
                {
                    // 相对角色键从 0 开始，默认勾选敌方第 1 个。
                    PreviewTargets = new List<string> { "$enemy:0" },
                };
                break;
            }
            case TimelineClipType.SetHitTargets:
            {
                clip = new TimelineClipSetHitTargets();
                config = new TimelineClipSetHitTargetsConfig
                {
                    PreviewSource = "$caster",
                    PreviewTargets = new List<string> { "$enemy:0" },
                };
                break;
            }
            case TimelineClipType.Force:
                clip = new TimelineClipForce();
                config = new TimelineClipForceConfig()
                {
                    Type = TimelineClipType.Force,
                    Force = new Vector3(0f, 0f, 5f),
                };
                break;
            case TimelineClipType.Follow:
                clip = new TimelineClipFollow();
                config = new TimelineClipFollowConfig()
                {
                    Type = TimelineClipType.Follow,
                    MoveEntityType = TimelineMoveEntityType.Camera,
                    FollowNodeType = UnitNodeType.Root,
                    FollowLagTime = 0f,
                };
                break;
            case TimelineClipType.Move:
                clip = new TimelineClipMove();
                config = new TimelineClipMoveConfig()
                {
                    Type = TimelineClipType.Move,
                    MoveEntityType = TimelineMoveEntityType.Camera,
                    OffestPos = new Vector3(0, 1.58f, -2.31f),
                    OffsetEuler = new Vector3(20f, 0f, 0f),
                    LockCameraUpright = true,
                };
                break;
            case TimelineClipType.SceneDarken:
                clip = new TimelineClipSceneDarken();
                config = new TimelineClipSceneDarkenConfig()
                {
                    Type = TimelineClipType.SceneDarken,
                    StartStrength = 0f,
                    EndStrength = 1f,
                };
                break;
            case TimelineClipType.RoleRotateToTarget:
                clip = new TimelineClipRoleRotateToTarget();
                config = new TimelineClipRoleRotateToTargetConfig()
                {
                    Type = TimelineClipType.RoleRotateToTarget,
                    Duration = 0f,
                };
                break;
            case TimelineClipType.HitStop:
                clip = new TimelineClipHitStop();
                config = new TimelineClipHitStopConfig()
                {
                    Type = TimelineClipType.HitStop,
                    Duration = 0.1f,
                };
                break;
            case TimelineClipType.SetSkybox:
                clip = new TimelineClipSetSkybox();
                config = new TimelineClipSetSkyboxConfig()
                {
                    Type = TimelineClipType.SetSkybox,
                    SkyboxMaterialPath =
                        "Assets/Package_Scene/Skybox/Sky_Anime_01_Day_a/Sky_Anime_01_Day_a.mat",
                };
                break;
            case TimelineClipType.Visibility:
                clip = new TimelineClipVisibility();
                config = new TimelineClipVisibilityConfig()
                {
                    Type = TimelineClipType.Visibility,
                    Show = true,
                    TargetType = TimelineVisibilityTargetType.Caster,
                    BattleExcludeTypes = TimelineClipVisibilityConfig.DefaultBattleExcludeTypes,
                };
                break;
            case TimelineClipType.HideBattleUI:
                clip = new TimelineClipHideBattleUI();
                config = new TimelineClipHideBattleUIConfig()
                {
                    Type = TimelineClipType.HideBattleUI,
                    Duration = 0f,
                    HideLayers = TimelineClipHideBattleUIConfig.DefaultHideLayers,
                    ResetOnEnd = false,
                };
                break;
            default:
                break;
        }

        if (config != null)
        {
            config.ClipKey = TimelineKeys.GenerateClipKey();
            config.PreKey = "";
            config.StartTime = defaultStartTime;
            config.DelayTime = 0f;
            if (type != TimelineClipType.HitStop
                && type != TimelineClipType.CameraPathAnim
                && type != TimelineClipType.HideBattleUI
                && type != TimelineClipType.SetCastTargets
                && type != TimelineClipType.SetHitTargets)
            {
                config.Duration = defaultDuration;
            }
            else if (type == TimelineClipType.HitStop && config.Duration <= 0f)
            {
                config.Duration = 0.1f;
            }

            clip.SetConfig(config);
        }

        return clip;
    }

    public bool CanAddClip(TimelineClipType type)
    {
        // 勿调用 CreateEmptyClip：右键菜单会对每个类型询问，创建实例极卡
        var types = UnitFeatureTypeDisplay.Types;
        for (var i = 0; i < types.Count; i++)
        {
            if (types[i] == type)
            {
                return true;
            }
        }

        return false;
    }

    public void InsertEmptyClipAndInit(TimelineClipType type, int index, float defaultStartTime = 0, float defaultDuration = 1)
    {
        if (TimelineEditorTargetUtil.ClipTypeRequiresSetTargets(type)
            && TimelineEditorTargetUtil.ShowMissingSetTargetsDialogIfNeeded(this))
        {
            return;
        }

        var clip = CreateEmptyClip(type, defaultStartTime, defaultDuration);
        if (clip == null)
        {
            return;
        }
        clip.PreInit(this, clip.Config);
        clip.Init(this, clip.Config);
        InsertClipAt(clip, index);
    }

    public void InsertClipFromCopiedConfig(TimelineClipConfigBase sourceConfig, int index, float startTime)
    {
        if (sourceConfig == null)
        {
            return;
        }

        if (TimelineEditorTargetUtil.ClipTypeRequiresSetTargets(sourceConfig.Type)
            && TimelineEditorTargetUtil.ShowMissingSetTargetsDialogIfNeeded(this))
        {
            return;
        }

        var json = TimelineJsonUtil.ToJson(sourceConfig);
        var config = TimelineJsonUtil.FromTimelineClipConfigJson(json);
        if (config == null)
        {
            return;
        }

        PreparePastedClipConfig(config, startTime);
        var clip = CreateClip(config);
        clip.PreInit(this, config);
        clip.Init(this, config);
        InsertClipAt(clip, index);
        timeline.Repaint();
    }

    private void InsertClipAt(TimelineClipBase clip, int index)
    {
        if (index > clips.Count - 1)
        {
            clips.Add(clip);
        }
        else
        {
            clips.Insert(index, clip);
        }

        sharedPlayback.Invalidate();
        timeline?.RefreshUnsavedChangesFlag();
    }

    internal static void PreparePastedClipConfig(TimelineClipConfigBase config, float startTime)
    {
        var oldClipKey = config.ClipKey;
        config.ClipKey = TimelineKeys.GenerateClipKey();
        config.PreKey = "";
        config.StartTime = startTime;
        if (config is TimelineClipVisibilityConfig visibility)
        {
            visibility.TargetBindingKey = null;
            visibility.EffectBindingKey = null;
        }
        if (config is TimelineClipCameraShotConfig shot)
        {
            TimelineEditorMcpBatch.ClearPivotBindings(shot);
        }

        if (config.EventConfigs == null)
        {
            return;
        }

        for (var i = 0; i < config.EventConfigs.Count; i++)
        {
            var eventConfig = config.EventConfigs[i];
            if (eventConfig == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(oldClipKey)
                && !string.IsNullOrEmpty(eventConfig.EventKey)
                && eventConfig.EventKey.StartsWith(oldClipKey + "_"))
            {
                eventConfig.EventKey = config.ClipKey + eventConfig.EventKey.Substring(oldClipKey.Length);
            }
            else
            {
                eventConfig.EventKey = TimelineKeys.GenerateEventKey();
            }
        }
    }

    public void RemoveClip(TimelineClipBase clip)
    {
        if (clip == null)
        {
            return;
        }

        if (pendingEndByOtherClipCatchOwner == clip)
        {
            pendingEndByOtherClipCatchOwner = null;
            endByOtherClipCatchFeedback = null;
        }

        if (pendingCameraShotBlendCatchOwner == clip)
        {
            pendingCameraShotBlendCatchOwner = null;
            endByOtherClipCatchFeedback = null;
        }

        ClearIncomingPreKeysForRemovedClip(clip);
        this.clips.Remove(clip);
        clip.OnDestroy();
        sharedPlayback.Invalidate();
        timeline.RefreshUnsavedChangesFlag();
        timeline.Repaint();
    }

    /// <summary>移除 Clip 前，清空其他 Clip 指向其 ClipKey 或内部 EventKey 的 PreKey / EndByOtherClipEnd。</summary>
    private void ClearIncomingPreKeysForRemovedClip(TimelineClipBase clip)
    {
        if (clip.Config == null)
        {
            return;
        }

        var preKeys = new HashSet<string>();
        if (!string.IsNullOrEmpty(clip.Config.ClipKey))
        {
            preKeys.Add(clip.Config.ClipKey);
        }

        var events = clip.GetAllEvents();
        for (int i = 0; i < events.Count; i++)
        {
            var eventKey = events[i].Config?.EventKey;
            if (!string.IsNullOrEmpty(eventKey))
            {
                preKeys.Add(eventKey);
            }
        }

        if (preKeys.Count == 0)
        {
            return;
        }

        for (int i = 0; i < clips.Count; i++)
        {
            var other = clips[i];
            if (other == clip || other.Config == null)
            {
                continue;
            }

            if (preKeys.Contains(other.Config.PreKey))
            {
                other.Config.PreKey = "";
            }

            if (!string.IsNullOrEmpty(other.Config.EndByOtherClipEnd)
                && preKeys.Contains(other.Config.EndByOtherClipEnd))
            {
                other.Config.EndByOtherClipEnd = "";
            }

            if (other.Config is TimelineClipVisibilityConfig visibility)
            {
                if (visibility.TargetBindingKey == clip.Config.ClipKey)
                {
                    visibility.TargetBindingKey = null;
                }
                if (visibility.EffectBindingKey == clip.Config.ClipKey)
                {
                    visibility.EffectBindingKey = null;
                }
            }

            if (other.Config is TimelineClipCameraShotConfig shot)
            {
                TimelineEditorMcpBatch.ClearPivotBindings(shot, clip.Config.ClipKey);
            }

            if (other.Config is TimelineClipCameraShotBlendConfig blendConfig
                && !string.IsNullOrEmpty(blendConfig.NextCameraShotClipKey)
                && preKeys.Contains(blendConfig.NextCameraShotClipKey))
            {
                blendConfig.NextCameraShotClipKey = "";
            }
        }
    }

    #region 点击判断


    /// <summary>�ж���Ļ�����Ƿ����ھ����ڡ�</summary>
    public bool Contains(Rect rect, Vector2 screenPoint)
    {
        return rect.Contains(screenPoint);
    }

    #endregion

    public void OnClipSelect(TimelineClipBase selectClip)
    {
        TryCompleteEndByOtherClipCatch(selectClip);
        TryCompleteCameraShotBlendCatch(selectClip);
        selectClip.ChangeClipOperateStateType(TimelineClipBase.ClipOperateStateType.Selected);
    }

    /// <summary>选中状态变化后刷新属性、画布与场景手柄；暂停时也立即反馈。</summary>
    public void NotifySelectionChanged()
    {
        timeline.Repaint();
        SceneView.RepaintAll();
    }

    public void OnEventSelect(TimelineEventBase selectEvent)
    {
        selectEvent.ChangeClipOperateStateType(TimelineEventBase.ClipOperateStateType.Selected);
    }

    internal void SelectEventProgrammatically(TimelineEventBase target)
    {
        SelectClipProgrammatically(target.Clip);
        selectEvent = target;
        OnEventSelect(target);
        timeline.Repaint();
    }

    internal void SelectNodeProgrammatically(TimelineNodeBase target)
    {
        SelectClipProgrammatically(target.Clip);
        selectNode = target;
        OnNodeSelect(target);
        timeline.Repaint();
    }

    public void OnNodeSelect(TimelineNodeBase selectNode)
    {
        selectNode.ChangeNodeOperateStateType(TimelineNodeBase.NodeOperateStateType.Selected);
    }

    private TimelineNodeBase nodeDragConnectSource;
    private TimelineLineBase nodeDragLine = new TimelineLineBase();

    public void SetNodeDragConnectSource(TimelineNodeBase node)
    {
        nodeDragConnectSource = node;
    }

    public void ClearNodeDragConnectSource()
    {
        nodeDragConnectSource = null;
    }

    public bool IsNodeDragConnecting => nodeDragConnectSource != null;

    private void NodeDragConnectLogic()
    {
        if (nodeDragConnectSource == null || nodeDragConnectSource.Clip == null)
        {
            return;
        }

        if (nodeDragConnectSource.Kind == TimelineNodeKind.End)
        {
            Vector2 startPos = nodeDragConnectSource.Clip.GetEndLinePoint();
            nodeDragLine.Draw(startPos, new Vector2(1, 0), Event.current.mousePosition, new Vector2(-1, 0));
            return;
        }

        if (nodeDragConnectSource.Kind == TimelineNodeKind.Start)
        {
            Vector2 startPos = nodeDragConnectSource.Clip.GetPreLinePoint();
            nodeDragLine.Draw(startPos, new Vector2(-1, 0), Event.current.mousePosition, new Vector2(1, 0));
        }
    }

    public TimelineClipBase FindClipAtLocalPoint(Vector2 localPoint)
    {
        for (int i = clips.Count - 1; i >= 0; i--)
        {
            if (Contains(clips[i].Rect, localPoint))
            {
                return clips[i];
            }
        }
        return null;
    }

    public TimelineNodeBase FindStartNodeAtLocalPoint(Vector2 localPoint)
    {
        return FindNodeAtLocalPoint(localPoint, TimelineNodeKind.Start);
    }

    public TimelineNodeBase FindEndNodeAtLocalPoint(Vector2 localPoint)
    {
        return FindNodeAtLocalPoint(localPoint, TimelineNodeKind.End);
    }

    private TimelineNodeBase FindNodeAtLocalPoint(Vector2 localPoint, TimelineNodeKind kind)
    {
        for (int i = 0; i < clips.Count; i++)
        {
            var nodes = clips[i].Nodes;
            for (int j = 0; j < nodes.Count; j++)
            {
                var node = nodes[j];
                if (node.Kind == kind && node.ShouldDraw() && node.ContainsScreenPoint(localPoint))
                {
                    return node;
                }
            }
        }
        return null;
    }

    public void ConnectClipsByPreKey(TimelineClipBase fromClip, TimelineClipBase toClip)
    {
        if (fromClip == null || toClip == null || fromClip == toClip)
        {
            return;
        }

        toClip.Config.PreKey = fromClip.Config.ClipKey;
        timeline.Repaint();
    }

    public void ConnectClipByEventPreKey(TimelineEventBase fromEvent, TimelineClipBase toClip)
    {
        if (fromEvent == null || toClip == null || fromEvent.Config == null)
        {
            return;
        }

        toClip.Config.PreKey = fromEvent.Config.EventKey;
        timeline.Repaint();
    }

    public TimelineEventBase FindEventAtLocalPoint(Vector2 localPoint)
    {
        var allEvents = Clips.SelectMany(o => o.GetAllEvents()).ToList();
        for (int i = 0; i < allEvents.Count; i++)
        {
            if (Contains(allEvents[i].Rect, localPoint))
            {
                return allEvents[i];
            }
        }
        return null;
    }

    public void TryCompleteNodeDragConnect(TimelineNodeBase sourceNode)
    {
        if (sourceNode == null || sourceNode.Clip == null)
        {
            return;
        }

        var localMouse = Event.current.mousePosition;
        if (sourceNode.Kind == TimelineNodeKind.End)
        {
            var targetStartNode = FindStartNodeAtLocalPoint(localMouse);
            if (targetStartNode != null && targetStartNode.Clip != null && targetStartNode.Clip != sourceNode.Clip)
            {
                ConnectClipsByPreKey(sourceNode.Clip, targetStartNode.Clip);
                return;
            }

            var targetClip = FindClipAtLocalPoint(localMouse);
            if (targetClip != null && targetClip != sourceNode.Clip)
            {
                ConnectClipsByPreKey(sourceNode.Clip, targetClip);
            }
            return;
        }

        if (sourceNode.Kind == TimelineNodeKind.Start)
        {
            var targetEvent = FindEventAtLocalPoint(localMouse);
            if (targetEvent != null)
            {
                ConnectClipByEventPreKey(targetEvent, sourceNode.Clip);
                return;
            }

            var targetEndNode = FindEndNodeAtLocalPoint(localMouse);
            if (targetEndNode != null && targetEndNode.Clip != null && targetEndNode.Clip != sourceNode.Clip)
            {
                ConnectClipsByPreKey(targetEndNode.Clip, sourceNode.Clip);
                return;
            }

            var targetClip = FindClipAtLocalPoint(localMouse);
            if (targetClip != null && targetClip != sourceNode.Clip)
            {
                ConnectClipsByPreKey(targetClip, sourceNode.Clip);
            }
        }
    }

    public bool IsSelectSomething(ref TimelineClipBase clip, ref TimelineEventBase e, ref TimelineNodeBase node, ref bool inDrawRect, MouseEventClickType clickType)
    {
        var evt = Event.current;
        if (this.MouseClickType != clickType)
        {
            return false;
        }


        var allNodes = Clips.SelectMany(o => o.Nodes).ToList();
        for (int i = 0; i < allNodes.Count; i++)
        {
            if (allNodes[i].ShouldDraw() && allNodes[i].ContainsScreenPoint(evt.mousePosition))
            {
                clip = null;
                e = null;
                node = allNodes[i];
                inDrawRect = false;
                return true;
            }
        }

        var allEvents = Clips.SelectMany(o => o.GetAllEvents()).ToList();
        for (int i = 0; i < allEvents.Count; i++)
        {
            if (Contains(allEvents[i].Rect, evt.mousePosition))
            {
                clip = null;
                e = allEvents[i];
                node = null;
                inDrawRect = false;
                return true;
            }
        }

        for (int i = 0; i < Clips.Count; i++)
        {
            if (Contains(Clips[i].Rect, evt.mousePosition))
            {
                clip = Clips[i];
                e = null;
                node = null;
                inDrawRect = false;
                return true;
            }
        }

        if (Contains(this.timeline.GetTimeArea.drawRect, this.timeline.LastMousePosition))
        {
            clip = null;
            e = null;
            node = null;
            inDrawRect = true;
            return true;
        }
        return false;


    }

    public void OnStopPreview(bool resetSceneView = true)
    {
        sharedPlayback.Stop();
        previewRefresh.Cancel();
        SceneDarkenUtil.SetMultiplyColor(Color.white);
        SetOriginCamera(resetSceneView);
        targetControl.OnStopPreView();
        unitsControl.OnStopPreView();
        for (int i = 0; i < clips.Count; i++)
        {
            clips[i].OnStopPreview();
        }
        unitBindings.Restore();
    }



    public MouseEventClickType MouseClickType { get; private set; }

    public enum MouseEventClickType
    {
        None,
        MouseLeftDown,
        MouseLeftUp,
        MouseRightDown,
        MouseRightUp,
    }

    private void MouseClickTypeLogic()
    {
        MouseClickType = MouseEventClickType.None;
        // 1. 获取当前事件
        Event currentEvent = Event.current;
        Rect windowRect = new Rect(0, 0, this.timeline.GetTimeArea.drawRect.width, this.timeline.GetTimeArea.drawRect.height); // 代表整个窗口区域

        if (currentEvent.type == EventType.MouseDown)
        {
            if (currentEvent.button == 0)
            {
                MouseClickType = MouseEventClickType.MouseLeftDown;
            }
            else if (currentEvent.button == 1)
            {
                MouseClickType = MouseEventClickType.MouseRightDown;
            }
        }
        else if(currentEvent.type == EventType.MouseUp)
        {
            if (currentEvent.button == 0)
            {
                MouseClickType = MouseEventClickType.MouseLeftUp;
            }
            else if (currentEvent.button == 1)
            {
                MouseClickType = MouseEventClickType.MouseRightUp;
            }
        }
        else if (currentEvent.type == EventType.ContextClick)
        {
            MouseClickType = MouseEventClickType.MouseRightUp;
        }
    }

    private float GetBoundClipEndTime(TimelineConfig config)
    {
        if (config == null || string.IsNullOrEmpty(config.DurationBindClipKey) || config.Clips == null)
        {
            return 0f;
        }

        for (int i = 0; i < this.clips.Count; i++)
        {
            var clip = this.clips[i];
            if (clip != null && clip.Config.ClipKey == config.DurationBindClipKey)
            {
                return ResolveClipContentEndTime(clip.Config);
            }
        }

        return 0f;
    }

    public TimelineClipConfigBase FindClipConfigByKey(string clipKey)
    {
        if (config?.Clips == null || string.IsNullOrEmpty(clipKey))
        {
            return null;
        }

        for (int i = 0; i < config.Clips.Count; i++)
        {
            var clipConfig = config.Clips[i];
            if (clipConfig != null && clipConfig.ClipKey == clipKey)
            {
                return clipConfig;
            }
        }

        return null;
    }

    public float ResolveClipDuration(TimelineClipConfigBase clipConfig)
    {
        if (clipConfig == null)
        {
            return 0f;
        }

        var contentStart = clipConfig.StartTime + clipConfig.DelayTime;
        return Mathf.Max(0f, ResolveClipContentEndTime(clipConfig) - contentStart);
    }

    /// <summary>
    /// 预览内容终点：所有 Clip 内容结束时间的最大值（含 EndByOtherClipEnd）。
    /// 无 Clip 时返回 0。用于录制自动停，避免硬跑到灰色 CutOff（默认 15s）。
    /// </summary>
    public float ResolvePreviewContentEndTime()
    {
        if (clips == null || clips.Count == 0)
        {
            return 0f;
        }

        var maxEnd = 0f;
        for (var i = 0; i < clips.Count; i++)
        {
            var clipConfig = clips[i]?.Config;
            if (clipConfig == null)
            {
                continue;
            }

            var end = ResolveClipContentEndTime(clipConfig);
            if (end > maxEnd)
            {
                maxEnd = end;
            }
        }

        return maxEnd;
    }

    public void SyncClipDurationFromCatch(TimelineClipConfigBase clipConfig)
    {
        if (clipConfig == null || string.IsNullOrEmpty(clipConfig.EndByOtherClipEnd))
        {
            return;
        }

        var duration = ResolveClipDuration(clipConfig);
        if (!Mathf.Approximately(clipConfig.Duration, duration))
        {
            clipConfig.Duration = duration;
        }
    }

    private void SyncAllCatchClipDurations()
    {
        if (clips == null)
        {
            return;
        }

        for (int i = 0; i < clips.Count; i++)
        {
            SyncClipDurationFromCatch(clips[i]?.Config);
        }
    }

    public float ResolveClipContentEndTime(TimelineClipConfigBase clipConfig)
    {
        return ResolveClipContentEndTime(clipConfig, null);
    }

    private float ResolveClipContentEndTime(TimelineClipConfigBase clipConfig, HashSet<string> visiting)
    {
        if (clipConfig == null)
        {
            return 0f;
        }

        if (!string.IsNullOrEmpty(clipConfig.EndByOtherClipEnd))
        {
            visiting ??= new HashSet<string>();
            if (visiting.Add(clipConfig.ClipKey))
            {
                var boundConfig = FindClipConfigByKey(clipConfig.EndByOtherClipEnd);
                if (boundConfig != null)
                {
                    return ResolveClipContentEndTime(boundConfig, visiting);
                }
            }
        }

        return clipConfig.StartTime + clipConfig.DelayTime + clipConfig.Duration;
    }
}
