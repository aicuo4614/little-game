using DMTimeArea;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 时间轴编辑器主窗口，负责布局、预览播放、Timeline JSON 读写及画布交互。
/// </summary>
public class EditorTimeLine : SimpleTimeArea
{
    #region 布局 Rect 与输入状态

    private Rect rectTotalArea;
    private Rect rectContent;
    private Rect rectTimeRuler;
    private Rect rectTopBar;
    private Rect rectLeft;
    public Rect rectLeftTopToolBar;

    private Vector2 lastMousePosition;
    public Vector2 LastMousePosition => lastMousePosition;

    private Event currentEvent;
    public Event CurrentEvent => currentEvent;

    /// <summary>鼠标点击在时间轴画布外时为 true，用于跳过 Clip 绘制逻辑。</summary>
    public bool IsSelectNoDrawRect = false;

    #endregion

    #region 时间轴运行状态

    private float _lastUpdateTime = 0f;

    private double runningTime = -1;
    public override double RunningTime
    {
        get { return runningTime; }
        set { runningTime = value; }
    }

    private static double cutOffTime = 15.0f;
    public override double CutOffTime
    {
        get { return cutOffTime; }
        set { cutOffTime = value; }
    }

    public bool IsPlaying { get; set; }

    protected override bool IsLockedMoveFrame
    {
        get { return (IsPlaying || Application.isPlaying); }
    }

    protected override bool IsLockDragHeaderArrow
    {
        get { return IsPlaying; }
    }

    #endregion

    #region 左侧面板

    private float leftPanelWidth = 300f;
    private Vector2 leftPanelScrollPosition;
    private bool isResizingLeftPanel;
    private const float MinLeftPanelWidth = 200f;
    private const float MaxLeftPanelWidth = 800f;
    private const float LeftPanelResizeHandleWidth = 6f;

    #endregion

    #region 视图常量与 EditorPrefs

    /// <summary>初始可见时间范围（秒）。范围越小，横向每秒钟占的像素越多，Clip 看起来越“长”。等价于 Ctrl+滚轮 后的 m_Scale.x。</summary>
    private const float InitialShownTimeMin = 0f;
    private const float InitialShownTimeMax = 3f;

    /// <summary>初始可见垂直范围。范围越大，纵向缩放越小，每行 Clip 越“扁”。等价于 Shift+滚轮 后的 m_Scale.y。</summary>
    private const float InitialShownYMin = 0f;
    private const float InitialShownYMax = 150f;

    /// <summary>Clip 行在垂直轴上的步长，越小行距越密、Clip 越扁。</summary>
    private const int ClipRowStep = 10;

    private static readonly Color RowStripeColorA = new Color(0x38 / 255f, 0x38 / 255f, 0x38 / 255f, 1f);
    private static readonly Color RowStripeColorB = new Color(0x2F / 255f, 0x2F / 255f, 0x2F / 255f, 1f);

    private const string ViewStateHasPrefsKey = "EditorTimeLine.ViewState.HasSaved";
    private const string ViewStateShownXMinPrefsKey = "EditorTimeLine.ViewState.ShownXMin";
    private const string ViewStateShownYMaxPrefsKey = "EditorTimeLine.ViewState.ShownYMax";
    private const string ViewStateScaleXPrefsKey = "EditorTimeLine.ViewState.ScaleX";
    private const string ViewStateScaleYPrefsKey = "EditorTimeLine.ViewState.ScaleY";
    private const string ViewStateTranslationXPrefsKey = "EditorTimeLine.ViewState.TranslationX";
    private const string ViewStateTranslationYPrefsKey = "EditorTimeLine.ViewState.TranslationY";
    private const string LeftPanelWidthPrefsKey = "EditorTimeLine.LeftPanelWidth";

    private bool timeAreaViewInitialized;
    private Vector2 lastPersistedScale;
    private Vector2 lastPersistedTranslation;
    private bool hasLastPersistedTransform;

    #endregion

    #region SimpleTimeArea 覆写属性

    public override Rect _rectTimeAreaTotal
    {
        get { return rectTotalArea; }
    }

    public override Rect _rectTimeAreaContent
    {
        get { return rectContent; }
    }

    public override Rect _rectTimeAreaRuler
    {
        get { return rectTimeRuler; }
    }

    protected override float sequencerHeaderWidth
    {
        get { return leftPanelWidth; }
    }

    #endregion

    #region TimelineControl 与文件路径

    private TimelineControl timelineControl;
    public TimelineControl TimelineControl => timelineControl;

    private string currentTimelineFilePath;
    public string CurrentTimelineFilePath => currentTimelineFilePath;

    private const string DefaultTimelineFolder = "Assets/Package_Unit/Timeline/Base";
    private const string LastTimelinePathPrefsKey = "Demo.EditorTimeLine.LastTimelineJsonPath";
    [NonSerialized]
    private bool timelineRestoreAttempted;
    private bool firstCreateControl = true;
    [NonSerialized]
    private bool reloadingAssembly;

    private TimelinePreviewRecorder previewRecorder = new TimelinePreviewRecorder();
    private Action<string, object> pendingRecordCompleted;

    /// <summary>是否正在预览录制（录制播放 / MCP record）。</summary>
    public bool IsRecording => previewRecorder != null && previewRecorder.IsRecording;

    /// <summary>最近一次加载/保存成功时的技能 JSON，用来对比是否有未保存改动。</summary>
    [NonSerialized]
    private string savedTimelineJsonSnapshot;

    /// <summary>新建且尚未存盘时为 true，始终视为脏。</summary>
    [NonSerialized]
    private bool newTimelineNeverSaved;

    #endregion

    private EditorTimelineMono editorTimelineMono;

    #region 窗口入口与生命周期

    private const string RequiredScenePath = "Assets/Scripts/Editor/TimelineEditor/Scene/TimelineEditor.unity";
    private const string PreviousScenePathPrefsKey = "EditorTimeLine.PreviousScenePath";
    private const string AssemblyReloadStateKey = "EditorTimeLine.AssemblyReloadState";

    [InitializeOnLoadMethod]
    private static void ScheduleReloadRecovery()
    {
        if (HasReloadState())
        {
            EditorApplication.delayCall += RecoverWindowAfterReload;
        }
    }

    private static bool HasReloadState() => !string.IsNullOrEmpty(SessionState.GetString(AssemblyReloadStateKey, string.Empty));

    private static bool IsPreviewSceneActive() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == RequiredScenePath;

    private static void RecoverWindowAfterReload()
    {
        // 仅恢复仍在技能预览场景中的工作区，编译完成后不会切换用户当前场景。
        if (!HasReloadState() || !IsPreviewSceneActive())
        {
            return;
        }

        var window = Resources.FindObjectsOfTypeAll<EditorTimeLine>().FirstOrDefault();
        if (!window)
        {
            window = GetWindow<EditorTimeLine>(false, "技能编辑器");
        }
        window.EnsureTimelineControl();
    }

    [MenuItem("Kabu/技能编辑器", false, 2002)]
    public static void DoWindow()
    {
        // 当前场景不是 TimelineEditor 时，先切换过去
        var currentScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (currentScene.path != RequiredScenePath)
        {
            // 记录打开技能编辑器前的场景路径，关闭时自动恢复
            if (!string.IsNullOrEmpty(currentScene.path))
                EditorPrefs.SetString(PreviousScenePathPrefsKey, currentScene.path);

            if (currentScene.isDirty)
                UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(RequiredScenePath);
            // OpenScene 可能让 EditorTimeLine 的 OnEnable/OnDisable 提前触发，
            // 因此必须在场景切换完毕后重新获取窗口引用。
        }

        // 编辑器预览：BattleCamera.ContentTrans 作为 URP Base，Game 视图可见
        EditorTimelineUtil.EnsureBattleCameraGameViewStack();

        var window = GetWindow<EditorTimeLine>(false, "技能编辑器");
        window.minSize = new Vector3(400f, 200f);
        window.EnsureTimelineControl();
        window.Show();
        window.RefreshWindowTitle();
        TimelinePortraitPreviewWindow.Open();
    }

    private void OnEnable()
    {
        AssemblyReloadEvents.beforeAssemblyReload += PrepareForAssemblyReload;
        if (HasReloadState())
        {
            // 被停靠在后台的窗口可能没有 OnGUI，也需要恢复场景预览。
            EditorApplication.delayCall += EnsureTimelineControl;
        }

        EditorApplication.update = (EditorApplication.CallbackFunction)System.Delegate.Combine(EditorApplication.update, new EditorApplication.CallbackFunction(OnEditorUpdate));
        SceneView.duringSceneGui -= OnTimelineSceneGUI;
        SceneView.duringSceneGui += OnTimelineSceneGUI;
        _lastUpdateTime = (float)EditorApplication.timeSinceStartup;
        leftPanelWidth = Mathf.Clamp(
            EditorPrefs.GetFloat(LeftPanelWidthPrefsKey, leftPanelWidth),
            MinLeftPanelWidth,
            MaxLeftPanelWidth);
        // OpenScene / Domain Reload 后必须重新套用 Prefs；未初始化完成时禁止 Persist 默认 0~5 视图
        timeAreaViewInitialized = false;
        hasLastPersistedTransform = false;
        if (IsPreviewSceneActive())
        {
            EditorTimelineUtil.EnsureBattleCameraGameViewStack();
        }
        saveChangesMessage = "当前技能尚未保存。是否保存？";
        RefreshWindowTitle();
    }

    private void BindSceneGizmos()
    {
        if (editorTimelineMono || !IsPreviewSceneActive())
        {
            return;
        }
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.TryGetComponent<EditorTimelineMono>(out var mono))
            {
                editorTimelineMono = mono;
                mono.Init(DrawTimelineGizmos);
                return;
            }
        }
    }

    private void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= PrepareForAssemblyReload;
        EditorApplication.delayCall -= EnsureTimelineControl;
        EditorApplication.update = (EditorApplication.CallbackFunction)System.Delegate.Remove(EditorApplication.update, new EditorApplication.CallbackFunction(OnEditorUpdate));
        SceneView.duringSceneGui -= OnTimelineSceneGUI;
        if (!reloadingAssembly && IsPreviewSceneActive())
        {
            StopPreView();
        }
        PersistTimeAreaViewState();
        PersistLastTimelinePath();
        // 丢弃内存中的 TimeArea，下次 OnEnable/OnGUI 从 Prefs 重建，避免场景切换后残留未恢复状态
        _simpleTimeArea = null;
        timeAreaViewInitialized = false;
        hasLastPersistedTransform = false;
        if (editorTimelineMono != null)
        {
            editorTimelineMono.Clear();
            editorTimelineMono = null;
        }
    }

    private void OnDestroy()
    {
        PersistTimeAreaViewState();
        ReleaseTimelineControl();
        if (!reloadingAssembly)
        {
            RestorePreviousSceneIfNeeded();
        }
    }

    private void PrepareForAssemblyReload()
    {
        reloadingAssembly = true;
        if (timelineControl is not TimelineControl control)
        {
            return;
        }

        try
        {
            var draft = control.CaptureDocument();
            var point = control.GetSelectedEvent();
            var node = control.GetSelectedNode();
            var clip = point?.Clip ?? node?.Clip ?? control.GetSelectedClip();
            var state = new JObject
            {
                ["timeline"] = TimelineJsonUtil.ToJson(draft, false),
                ["path"] = currentTimelineFilePath,
                ["savedJson"] = savedTimelineJsonSnapshot,
                ["savedFile"] = savedTimelineFileContents,
                ["newDocument"] = newTimelineNeverSaved,
                ["casterTeam"] = (int)control.PreviewCasterTeam,
                ["casterPath"] = control.MyTransformPath,
                ["runningTime"] = RunningTime,
                ["cutOffTime"] = CutOffTime,
                ["clipKey"] = clip?.Config.ClipKey,
                ["eventKey"] = point?.Config.EventKey,
                ["nodeKind"] = (int?)node?.Kind,
                ["inspectorScrollX"] = leftPanelScrollPosition.x,
                ["inspectorScrollY"] = leftPanelScrollPosition.y,
                ["mcpHistory"] = TimelineEditorMcpAuthoring.CaptureReloadHistory(this, draft)
            };
            SessionState.SetString(AssemblyReloadStateKey, state.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            try
            {
                PausePreView();
                if (IsPreviewSceneActive())
                {
                    control.OnStopPreview(resetSceneView: false);
                }
            }
            finally
            {
                ReleaseTimelineControl();
            }
        }
    }

    private void ReleaseTimelineControl()
    {
        var control = timelineControl;
        timelineControl = null;
        control?.OnDestroy();
    }

    /// <summary>关闭窗口并选择保存时调用。另存为取消则保持窗口打开。</summary>
    public override void SaveChanges()
    {
        if (!TrySaveCurrentTimeline(false))
        {
            hasUnsavedChanges = true;
            RefreshWindowTitle();
            return;
        }

        base.SaveChanges();
        RefreshWindowTitle();
    }

    /// <summary>关闭窗口并选择不保存时清掉未保存标记，随后走原有销毁与切场景。</summary>
    public override void DiscardChanges()
    {
        newTimelineNeverSaved = false;
        base.DiscardChanges();
        RefreshWindowTitle();
    }

    private static void RestorePreviousSceneIfNeeded()
    {
        var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (activeScene.path != RequiredScenePath)
            return;

        var previousScenePath = EditorPrefs.GetString(PreviousScenePathPrefsKey, string.Empty);
        if (string.IsNullOrEmpty(previousScenePath) || previousScenePath == RequiredScenePath)
            return;

        if (activeScene.isDirty)
            UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previousScenePath);
        EditorPrefs.DeleteKey(PreviousScenePathPrefsKey);
    }

    #endregion

    #region Editor 更新循环

    /// <summary>上一帧已驱动 Scene 重绘时的 RunningTime；未变则不刷 Scene，避免空转吃满 GPU。</summary>
    private double lastSceneRepaintRunningTime = double.NaN;

    private void OnEditorUpdate()
    {
        if (reloadingAssembly || !IsPreviewSceneActive())
        {
            _lastUpdateTime = (float)EditorApplication.timeSinceStartup;
            return;
        }
        BindSceneGizmos();

        // 预览播放：按真实时间推进 RunningTime
        if (!Application.isPlaying && this.IsPlaying)
        {
            double fTime = (float)EditorApplication.timeSinceStartup - _lastUpdateTime;
            this.RunningTime += Math.Abs(fTime) * 1.0f;
            var playStop = CutOffTime;
            if (IsRecording && previewRecorder.UntilCutoff && previewRecorder.AutoStopTime >= 0f)
            {
                playStop = previewRecorder.AutoStopTime;
            }

            if (this.RunningTime >= playStop)
            {
                this.PausePreView();
            }
        }

        SetRunningTimeLogic();

        var deltaTime = (float)EditorApplication.timeSinceStartup - _lastUpdateTime;
        timelineControl?.OnEditorUpdate(deltaTime);
        if (IsRecording)
        {
            previewRecorder.Tick((float)RunningTime, IsPlaying);
        }

        _lastUpdateTime = (float)EditorApplication.timeSinceStartup;

        // 空转时不要每 tick 刷 Timeline + Scene：选中「通用移动」时会连带 Camera.Render / Handles，极易把编辑器拖死
        var runningChanged = double.IsNaN(lastSceneRepaintRunningTime)
            || System.Math.Abs(RunningTime - lastSceneRepaintRunningTime) > 1e-6;
        if (IsPlaying || runningChanged)
        {
            lastSceneRepaintRunningTime = RunningTime;
            Repaint();
            SceneView.RepaintAll();
        }
    }

    #endregion

    #region OnGUI 主流程

    private void OnGUI()
    {
        if (reloadingAssembly || !IsPreviewSceneActive())
        {
            return;
        }
        EnsureTimelineControl();
        if (timelineControl is null)
        {
            EditorGUILayout.HelpBox("当前技能草稿已暂存，但暂时无法恢复。", MessageType.Warning);
            if (GUILayout.Button("重试恢复草稿"))
            {
                timelineRestoreAttempted = false;
                EnsureTimelineControl();
            }
            return;
        }
        currentEvent = Event.current;
        lastMousePosition = currentEvent.mousePosition;

        // 计算各区域 Rect
        Rect rectMainBodyArea = new Rect(0, toolbarHeight, base.position.width, this.position.height - toolbarHeight);
        rectTopBar = new Rect(0, 0, this.position.width, toolbarHeight);
        rectLeft = new Rect(
            rectMainBodyArea.x,
            rectMainBodyArea.y + timeRulerHeight,
            leftPanelWidth,
            rectMainBodyArea.height - timeRulerHeight);
        rectLeftTopToolBar = new Rect(rectMainBodyArea.x, rectMainBodyArea.y, leftPanelWidth, timeRulerHeight);
        rectTotalArea = new Rect(rectMainBodyArea.x + leftPanelWidth, rectMainBodyArea.y, base.position.width - leftPanelWidth, rectMainBodyArea.height);
        rectTimeRuler = new Rect(rectMainBodyArea.x + leftPanelWidth, rectMainBodyArea.y, base.position.width - leftPanelWidth, timeRulerHeight);
        rectContent = new Rect(rectMainBodyArea.x + leftPanelWidth, rectMainBodyArea.y + timeRulerHeight, base.position.width - leftPanelWidth, rectMainBodyArea.height - timeRulerHeight);

        HandleLeftPanelResize(rectMainBodyArea);

        InitTimeArea(false, false, true, true);
        timelineControl?.ShortcutControl.Handle();
        HandleConnectDragAutoScroll();
        HandleCanvasScrollWheelPan();
        DrawTimeAreaBackGround();

        IsSelectNoDrawRectLogic();
        GUI.BeginClip(GetTimeArea.drawRect);
        timelineControl.DrawH();
        DrawVerticalTickLine();
        timelineControl.Draw();
        GUI.EndClip();

        OnTimeRulerCursorAndCutOffCursorInput();
        DrawTimeRulerArea();

        DrawTopToolBar();
        DrawLeftContent();
        DrawLeftTopToolBar();

        if (firstCreateControl && this.timelineControl != null)
        {
            firstCreateControl = false;
            StopPreView();
        }

        // 缩放/平移一改就写入 Prefs，避免只依赖 OnDisable（场景切换时可能还没初始化完）
        PersistTimeAreaViewStateIfChanged();
        RefreshUnsavedChangesIfUserEdited();
    }

    private void DrawTimelineGizmos()
    {
        timelineControl?.OnDrawGizmos();
    }

    private void OnTimelineSceneGUI(SceneView sceneView)
    {
        timelineControl?.OnSceneGUI(sceneView);
        if (Event.current != null && Event.current.type == EventType.MouseUp)
        {
            RefreshUnsavedChangesFlag();
        }
    }

    /// <summary>判断当前鼠标事件是否发生在时间轴画布外。</summary>
    private void IsSelectNoDrawRectLogic()
    {
        IsSelectNoDrawRect = false;
        bool isSelected = false;
        if (currentEvent.type == EventType.MouseDown)
        {
            if (currentEvent.button == 0)
            {
                isSelected = true;
            }
            else if (currentEvent.button == 1)
            {
                isSelected = true;
            }
        }
        else if (currentEvent.type == EventType.MouseUp)
        {
            if (currentEvent.button == 0)
            {
                isSelected = true;
            }
            else if (currentEvent.button == 1)
            {
                isSelected = true;
            }
        }
        else if (currentEvent.type == EventType.ContextClick)
        {
            isSelected = true;
        }

        if (!isSelected)
        {
            IsSelectNoDrawRect = false;
        }
        else
        {
            IsSelectNoDrawRect = !this.GetTimeArea.drawRect.Contains(lastMousePosition);
        }
    }

    #endregion

    #region 画布绘制

    /// <summary>绘制 Clip 行的斑马纹背景。</summary>
    protected override void DrawVerticalTickLine()
    {
        var drawWidth = GetTimeArea.drawRect.width;
        var drawHeight = GetTimeArea.drawRect.height;
        if (drawWidth <= 0f || drawHeight <= 0f)
        {
            return;
        }

        var maxY = PixelToY(GetTimeArea.drawRect.y);
        var minY = PixelToY(GetTimeArea.drawRect.yMax);
        var list = GetSequenceInRange(YConst, GetCount(), minY, maxY);
        if (list.Count < 2)
        {
            EditorGUI.DrawRect(new Rect(0f, 0f, drawWidth, drawHeight), RowStripeColorA);
            return;
        }

        var boundaries = new List<(float localY, float worldY)>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            boundaries.Add((WorldPixelYToLocal(YToPixel(list[i])), list[i]));
        }
        boundaries.Sort((a, b) => a.localY.CompareTo(b.localY));

        float viewTop = 0f;
        float viewBottom = drawHeight;

        if (boundaries[0].localY > viewTop)
        {
            DrawRowStripeByWorldY(0f, boundaries[0].localY, PixelToY(LocalPixelYToWorld(0f)), boundaries[0].worldY, drawWidth);
        }

        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            DrawRowStripeByWorldY(boundaries[i].localY, boundaries[i + 1].localY, boundaries[i].worldY, boundaries[i + 1].worldY, drawWidth);
        }

        if (boundaries[boundaries.Count - 1].localY < viewBottom)
        {
            var last = boundaries[boundaries.Count - 1];
            DrawRowStripeByWorldY(last.localY, viewBottom, last.worldY, PixelToY(LocalPixelYToWorld(viewBottom)), drawWidth);
        }
    }

    private void DrawRowStripeByWorldY(float localY0, float localY1, float worldY0, float worldY1, float drawWidth)
    {
        float top = Mathf.Min(localY0, localY1);
        float bottom = Mathf.Max(localY0, localY1);
        top = Mathf.Max(top, 0f);
        bottom = Mathf.Min(bottom, GetTimeArea.drawRect.height);
        if (bottom <= top)
        {
            return;
        }

        var centerWorldY = (worldY0 + worldY1) * 0.5f;
        var rowIndex = GetIndexByY(centerWorldY);
        var color = (rowIndex & 1) == 0 ? RowStripeColorA : RowStripeColorB;
        EditorGUI.DrawRect(new Rect(0f, top, drawWidth, bottom - top), color);
    }

    private void DrawBorder(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    #endregion

    #region 坐标换算

    public float WorldPixelXToLocal(float pixelX)
    {
        Rect dr = GetTimeArea.drawRect;
        float localPixelX = pixelX - dr.x;
        return localPixelX;
    }

    public float WorldPixelYToLocal(float pixelY)
    {
        Rect dr = GetTimeArea.drawRect;
        float localPixelY = pixelY - dr.y;
        return localPixelY;
    }

    public float LocalPixelXToWorld(float localPixelX)
    {
        return localPixelX + GetTimeArea.drawRect.x;
    }

    public float LocalPixelYToWorld(float localPixelY)
    {
        return localPixelY + GetTimeArea.drawRect.y;
    }

    #endregion

    #region Clip 行布局

    /// <summary>将指定片段移到画布可见区域并闪烁提示，保留当前 Inspector 和游标。</summary>
    public void LocateClip(TimelineClipBase clip)
    {
        var area = GetTimeArea;
        var target = clip.GetLocateRect();
        // 长片段以开头附近为定位点，避免时间轴缩放变化。
        var focusX = target.x + Mathf.Min(target.width * 0.5f, area.drawRect.width * 0.25f);
        var offset = new Vector2(area.drawRect.width * 0.5f - focusX,
            area.drawRect.height * 0.5f - target.center.y);
        area.SetTransform(area.translation + offset, area.scale);
        clip.FlashForLocate();
        Repaint();
    }

    private int yConst = -1;

    /// <summary>当前可见区域顶部对齐的 Clip 行基准 Y 值。</summary>
    private int YConst
    {
        get
        {
            if (yConst == -1)
            {
                yConst = GetPreviousMultiple(PixelToY(GetTimeArea.drawRect.y), GetCount());
            }
            return yConst;
        }
    }

    public int GetCount()
    {
        return ClipRowStep;
    }

    public float GetYByIndex(int index)
    {
        return YConst - GetCount() * index;
    }

    public int GetIndexByY(float y)
    {
        return (int)((YConst - y) / GetCount());
    }

    public int GetIndexByMousePosition()
    {
        return GetIndexByY(PixelToY(lastMousePosition.y));
    }

    public float GetItemPixelHeight()
    {
        return Mathf.Abs(YToPixel(GetYByIndex(0)) - YToPixel(GetYByIndex(1)));
    }

    #endregion

    #region 视图缩放持久化

    private void ApplyInitialTimeAreaViewIfNeeded()
    {
        if (timeAreaViewInitialized)
        {
            return;
        }

        if (GetTimeArea == null || GetTimeArea.drawRect.width < 1f || GetTimeArea.drawRect.height < 1f)
        {
            return;
        }

        if (TryRestoreSavedTimeAreaView())
        {
            yConst = -1;
            timeAreaViewInitialized = true;
            CachePersistedTransform();
            return;
        }

        GetTimeArea.SetShownHRangeInsideMargins(InitialShownTimeMin, InitialShownTimeMax);
        GetTimeArea.SetShownVRangeInsideMargins(InitialShownYMin, InitialShownYMax);
        yConst = -1;
        timeAreaViewInitialized = true;
        CachePersistedTransform();
    }

    private bool TryRestoreSavedTimeAreaView()
    {
        if (!EditorPrefs.GetBool(ViewStateHasPrefsKey, false))
        {
            return false;
        }

        float scaleX = EditorPrefs.GetFloat(ViewStateScaleXPrefsKey, 0f);
        float scaleY = EditorPrefs.GetFloat(ViewStateScaleYPrefsKey, 0f);
        if (Mathf.Abs(scaleX) < 1E-05f || Mathf.Abs(scaleY) < 1E-05f)
        {
            return false;
        }

        // 优先还原 translation + scale（Ctrl/Shift 滚轮与中键平移的精确状态）
        if (EditorPrefs.HasKey(ViewStateTranslationXPrefsKey)
            && EditorPrefs.HasKey(ViewStateTranslationYPrefsKey))
        {
            var translation = new Vector2(
                EditorPrefs.GetFloat(ViewStateTranslationXPrefsKey, 0f),
                EditorPrefs.GetFloat(ViewStateTranslationYPrefsKey, 0f));
            GetTimeArea.RestoreScaleAndTranslation(translation, new Vector2(scaleX, scaleY));
            return true;
        }

        float shownXMin = EditorPrefs.GetFloat(ViewStateShownXMinPrefsKey, InitialShownTimeMin);
        float shownYMax = EditorPrefs.GetFloat(ViewStateShownYMaxPrefsKey, InitialShownYMax);
        GetTimeArea.RestoreAnchoredView(shownXMin, shownYMax, scaleX, scaleY);
        return true;
    }

    private void PersistTimeAreaViewStateIfChanged()
    {
        if (!timeAreaViewInitialized || _simpleTimeArea == null)
        {
            return;
        }

        var area = GetTimeArea;
        if (area == null || area.drawRect.width < 1f || area.drawRect.height < 1f)
        {
            return;
        }

        var scale = area.scale;
        var translation = area.translation;
        if (hasLastPersistedTransform
            && (scale - lastPersistedScale).sqrMagnitude < 1e-8f
            && (translation - lastPersistedTranslation).sqrMagnitude < 1e-8f)
        {
            return;
        }

        PersistTimeAreaViewState();
    }

    private void PersistTimeAreaViewState()
    {
        EditorPrefs.SetFloat(LeftPanelWidthPrefsKey, leftPanelWidth);

        // 未完成首次恢复前不要落盘：否则会把 Init 的默认 0~5s 视图盖掉用户上次的缩放/平移
        if (!timeAreaViewInitialized || _simpleTimeArea == null)
        {
            return;
        }

        var area = GetTimeArea;
        if (area == null || area.drawRect.width < 1f || area.drawRect.height < 1f)
        {
            return;
        }

        var shown = area.shownAreaInsideMargins;
        var scale = area.scale;
        var translation = area.translation;
        if (Mathf.Abs(scale.x) < 1E-05f || Mathf.Abs(scale.y) < 1E-05f)
        {
            return;
        }

        EditorPrefs.SetBool(ViewStateHasPrefsKey, true);
        EditorPrefs.SetFloat(ViewStateShownXMinPrefsKey, shown.xMin);
        EditorPrefs.SetFloat(ViewStateShownYMaxPrefsKey, shown.yMax);
        EditorPrefs.SetFloat(ViewStateScaleXPrefsKey, scale.x);
        EditorPrefs.SetFloat(ViewStateScaleYPrefsKey, scale.y);
        EditorPrefs.SetFloat(ViewStateTranslationXPrefsKey, translation.x);
        EditorPrefs.SetFloat(ViewStateTranslationYPrefsKey, translation.y);
        CachePersistedTransform();
    }

    private void CachePersistedTransform()
    {
        if (_simpleTimeArea == null)
        {
            hasLastPersistedTransform = false;
            return;
        }

        lastPersistedScale = GetTimeArea.scale;
        lastPersistedTranslation = GetTimeArea.translation;
        hasLastPersistedTransform = true;
    }

    protected override void OnTimeAreaRectUpdated()
    {
        ApplyInitialTimeAreaViewIfNeeded();
    }

    #endregion

    #region 连线拖拽自动滚动

    private const float ConnectDragAutoScrollEdgePixels = 32f;
    /// <summary>边缘自动滚动速度（像素/秒）。</summary>
    private const float ConnectDragAutoScrollMinSpeed = 50f;
    private const float ConnectDragAutoScrollMaxSpeed = 300;

    private double connectDragAutoScrollLastTime;

    private bool IsTimelineConnectDragging()
    {
        var menu = timelineControl?.MenuControl;
        if (menu != null && (menu.IsClipConnecting || menu.IsEventConnecting))
        {
            return true;
        }

        return timelineControl != null && timelineControl.IsNodeDragConnecting;
    }

    /// <summary>拖拽 Clip/Event/Node 连线时，鼠标靠近或超出 <see cref="rectContent"/> 上下边缘则自动垂直滚动画布。</summary>
    private void HandleConnectDragAutoScroll()
    {
        if (!IsTimelineConnectDragging())
        {
            connectDragAutoScrollLastTime = 0d;
            return;
        }

        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        var area = GetTimeArea;
        if (area == null || area.drawRect.height < 1f)
        {
            return;
        }

        var now = EditorApplication.timeSinceStartup;
        var deltaTime = connectDragAutoScrollLastTime <= 0d
            ? 0.016f
            : (float)(now - connectDragAutoScrollLastTime);
        connectDragAutoScrollLastTime = now;
        deltaTime = Mathf.Clamp(deltaTime, 0f, 0.05f);

        var pixelSpeed = GetConnectDragAutoScrollPixelSpeed(rectContent, lastMousePosition);
        if (Mathf.Abs(pixelSpeed) < 0.01f)
        {
            return;
        }

        var translation = area.translation;
        var scale = area.scale;
        translation.y += pixelSpeed * deltaTime;
        area.SetTransform(translation, scale);
    }

    private static float GetConnectDragAutoScrollPixelSpeed(Rect contentRect, Vector2 mousePosition)
    {
        float edge = ConnectDragAutoScrollEdgePixels;
        float intensity = 0f;

        if (mousePosition.y < contentRect.y)
        {
            intensity = Mathf.Clamp01((contentRect.y - mousePosition.y) / edge);
        }
        else if (mousePosition.y > contentRect.yMax)
        {
            intensity = -Mathf.Clamp01((mousePosition.y - contentRect.yMax) / edge);
        }
        else if (mousePosition.y < contentRect.y + edge)
        {
            intensity = 1f - (mousePosition.y - contentRect.y) / edge;
        }
        else if (mousePosition.y > contentRect.yMax - edge)
        {
            intensity = -(1f - (contentRect.yMax - mousePosition.y) / edge);
        }

        if (Mathf.Abs(intensity) < 0.0001f)
        {
            return 0f;
        }

        float speed = Mathf.Lerp(ConnectDragAutoScrollMinSpeed, ConnectDragAutoScrollMaxSpeed, Mathf.Abs(intensity));
        return Mathf.Sign(intensity) * speed;
    }

    #endregion

    #region 画布滚轮平移

    /// <summary>滚轮在画布上：上下平移；Ctrl/Shift+滚轮仍为缩放（见 ZoomableArea.OnAreaEvent）。</summary>
    private const float CanvasScrollWheelPixelsPerStep = 16f;

    private void HandleCanvasScrollWheelPan()
    {
        var evt = Event.current;
        if (evt.type != EventType.ScrollWheel)
        {
            return;
        }

        if (!rectContent.Contains(evt.mousePosition))
        {
            return;
        }

        if (evt.control || evt.shift || evt.alt)
        {
            return;
        }

        var area = GetTimeArea;
        if (area == null || area.drawRect.height < 1f)
        {
            return;
        }

        var translation = area.translation;
        translation.y -= evt.delta.y * CanvasScrollWheelPixelsPerStep;
        area.SetTransform(translation, area.scale);
        evt.Use();
        GUI.changed = true;
    }

    #endregion

    #region 左侧面板 UI

    protected virtual void DrawLeftContent()
    {
        GUILayout.BeginArea(rectLeft);
        // 不写死 Width/Height：填满 BeginArea，避免与 Area 尺寸双重计算导致点击热区相对绘制错位
        leftPanelScrollPosition = EditorGUILayout.BeginScrollView(leftPanelScrollPosition);
        if (timelineControl != null)
        {
            // 播放/停止栏下方：预览画质 / 施法阵营 + Clip 显示名搜索，再画属性
            timelineControl.DrawPreviewQualityBar();
            EditorGUILayout.Space(2f);
            timelineControl.DrawPreviewCasterTeamBar();
            EditorGUILayout.Space(2f);
            timelineControl.DrawClipSearchBar();
            EditorGUILayout.Space(4f);
            timelineControl.DrawInspector();
        }

        // 底部留白，避免最后一项贴边或被裁切
        EditorGUILayout.Space(24f);
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void HandleLeftPanelResize(Rect rectMainBodyArea)
    {
        var maxWidth = Mathf.Max(MinLeftPanelWidth, position.width - 120f);
        leftPanelWidth = Mathf.Clamp(leftPanelWidth, MinLeftPanelWidth, Mathf.Min(MaxLeftPanelWidth, maxWidth));

        var resizeRect = new Rect(
            leftPanelWidth - LeftPanelResizeHandleWidth * 0.5f,
            rectMainBodyArea.y,
            LeftPanelResizeHandleWidth,
            position.height - rectMainBodyArea.y);

        EditorGUIUtility.AddCursorRect(resizeRect, MouseCursor.ResizeHorizontal);

        var evt = Event.current;
        switch (evt.type)
        {
            case EventType.MouseDown:
                if (evt.button == 0 && resizeRect.Contains(evt.mousePosition))
                {
                    isResizingLeftPanel = true;
                    evt.Use();
                }
                break;
            case EventType.MouseDrag:
                if (isResizingLeftPanel)
                {
                    leftPanelWidth = Mathf.Clamp(leftPanelWidth + evt.delta.x, MinLeftPanelWidth, Mathf.Min(MaxLeftPanelWidth, maxWidth));
                    Repaint();
                    evt.Use();
                }
                break;
            case EventType.MouseUp:
                if (isResizingLeftPanel && evt.button == 0)
                {
                    isResizingLeftPanel = false;
                    evt.Use();
                }
                break;
        }
    }

    #endregion

    #region 顶部工具栏

    protected virtual void DrawTopToolBar()
    {
        GUILayout.BeginArea(rectTopBar);
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        DrawCustomToolBar();

        GUILayout.FlexibleSpace();

        if (!Application.isPlaying && GUILayout.Button(ResManager.SettingIcon, EditorStyles.toolbarButton, GUILayout.Width(30)))
        {
            OnClickSettingButton();
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawCustomToolBar()
    {
        if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(44)))
        {
            OnNewTimeline();
        }

        if (GUILayout.Button("加载", EditorStyles.toolbarButton, GUILayout.Width(44)))
        {
            OnLoadTimeline();
        }

        if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(44)))
        {
            SaveCurrentTimeline();
        }

        if (GUILayout.Button("另存为", EditorStyles.toolbarButton, GUILayout.Width(52)))
        {
            TrySaveTimelineAs(true);
        }

        if (GUILayout.Button(TimelineBindingEditorText.Validate, EditorStyles.toolbarButton, GUILayout.Width(44)))
        {
            var report = TimelineEditorMcpValidation.Report(timelineControl.CaptureDocument(), timelineControl);
            EditorUtility.DisplayDialog(TimelineBindingEditorText.ValidationTitle,
                TimelineEditorMcpValidation.Summary(report), TimelineBindingEditorText.Confirm);
        }

        if (GUILayout.Button(new GUIContent("快捷键", "查看技能编辑器快捷键说明"), EditorStyles.toolbarButton, GUILayout.Width(52)))
        {
            TimelineEditorShortcutControl.ShowHelpDialog();
        }
    }

    /// <summary>左侧时间轴播放控制栏：上一帧 / 播放 / 录制播放 / 下一帧 / 停止。</summary>
    private void DrawLeftTopToolBar()
    {
        GUILayout.BeginArea(rectLeftTopToolBar, string.Empty, EditorStyles.toolbarButton);
        GUILayout.BeginHorizontal();

        if (GUILayout.Button(ResManager.prevKeyContent, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
        {
            PreviousTimeFrame();
        }

        bool playing = IsPlaying;
        playing = GUILayout.Toggle(playing, ResManager.playContent, EditorStyles.toolbarButton, new GUILayoutOption[0]);
        if (!Application.isPlaying)
        {
            if (IsPlaying != playing)
            {
                IsPlaying = playing;
                if (IsPlaying)
                {
                    PlayPreview();
                }
                else
                {
                    PausePreView();
                }
            }
        }

        var recording = IsRecording;
        var nextRecording = GUILayout.Toggle(
            recording,
            new GUIContent(ResManager.recordContent.image, "录制播放：从头播放并采样场上 TRS，停止后复制 JSON 到剪切板"),
            EditorStyles.toolbarButton,
            GUILayout.ExpandWidth(false));
        if (!Application.isPlaying && nextRecording != recording)
        {
            if (nextRecording)
            {
                StartRecordingPlay(TimelinePreviewRecorder.DefaultSampleHz, untilCutoff: false, onCompleted: null);
            }
            else if (IsRecording)
            {
                var json = FinishRecording("userStop");
                if (!string.IsNullOrEmpty(json))
                {
                    EditorGUIUtility.systemCopyBuffer = json;
                    Debug.Log($"[技能编辑器] 预览录制已复制到剪切板（{json.Length} 字符）。");
                }

                StopPreView();
            }
        }

        if (GUILayout.Button(ResManager.nextKeyContent, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
        {
            NextTimeFrame();
        }

        if (GUILayout.Button(ResManager.StopIcon, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))
            && !Application.isPlaying)
        {
            if (IsRecording)
            {
                var json = FinishRecording("userStop");
                if (!string.IsNullOrEmpty(json))
                {
                    EditorGUIUtility.systemCopyBuffer = json;
                    Debug.Log($"[技能编辑器] 预览录制已复制到剪切板（{json.Length} 字符）。");
                }
            }

            StopPreView();
        }

        GUILayout.FlexibleSpace();
        if (IsRecording)
        {
            var old = GUI.color;
            GUI.color = new Color(1f, 0.35f, 0.35f, 1f);
            GUILayout.Label("REC");
            GUI.color = old;
        }

        string timeStr = TimeAsString((double)this.RunningTime, "F2");
        GUILayout.Label(timeStr);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    #endregion

    #region Timeline 加载保存

    private void EnsureTimelineControl()
    {
        if (reloadingAssembly || !IsPreviewSceneActive() || timelineControl is not null)
        {
            return;
        }

        if (!timelineRestoreAttempted)
        {
            timelineRestoreAttempted = true;
            if (HasReloadState())
            {
                TryRestoreAfterAssemblyReload();
                return;
            }

            if (TryLoadTimelineFromPath(GetLastTimelinePath(), false))
            {
                return;
            }
        }

        if (HasReloadState())
        {
            return;
        }

        CreateTimelineControl(CreateDefaultTimelineConfig());
        currentTimelineFilePath = null;
        MarkNewTimelineUnsaved();
    }

    private bool TryRestoreAfterAssemblyReload()
    {
        var json = SessionState.GetString(AssemblyReloadStateKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        try
        {
            var state = JObject.Parse(json);
            var config = TimelineJsonUtil.FromJson((string)state["timeline"]);
            var casterTeam = (UnitTeamType)(int)state["casterTeam"];
            CreateTimelineControl(config, casterTeam, resetSceneView: false);

            currentTimelineFilePath = (string)state["path"];
            savedTimelineJsonSnapshot = (string)state["savedJson"];
            savedTimelineFileContents = (string)state["savedFile"];
            newTimelineNeverSaved = (bool)state["newDocument"];
            CutOffTime = (double)state["cutOffTime"];
            leftPanelScrollPosition = new Vector2((float?)state["inspectorScrollX"] ?? 0f, (float?)state["inspectorScrollY"] ?? 0f);
            RestoreReloadSelection(state);
            PausePreView();
            SeekRunningTimeImmediate((double)state["runningTime"]);
            // 首次绘制通常会复位播放头，恢复时保留刚读回的位置。
            firstCreateControl = false;
            RefreshUnsavedChangesFlag();
            TimelineEditorMcpAuthoring.RestoreReloadHistory(this, state["mcpHistory"] as JObject);
            SessionState.EraseString(AssemblyReloadStateKey);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            ReleaseTimelineControl();
            return false;
        }
    }

    private void RestoreReloadSelection(JObject state)
    {
        var key = (string)state["clipKey"];
        var clip = timelineControl.Clips.Find(item => item.Config.ClipKey == key);
        if (clip is null)
        {
            return;
        }
        var eventKey = (string)state["eventKey"];
        var point = string.IsNullOrEmpty(eventKey) ? null : clip.GetAllEvents().FirstOrDefault(item => item.Config.EventKey == eventKey);
        if (point is not null)
        {
            timelineControl.SelectEventProgrammatically(point);
            return;
        }
        var nodeKind = (int?)state["nodeKind"];
        var node = clip.Nodes.Find(item => (int)item.Kind == nodeKind);
        if (node is not null)
        {
            timelineControl.SelectNodeProgrammatically(node);
            return;
        }
        timelineControl.SelectClipProgrammatically(clip);
    }

    private void CreateTimelineControl(TimelineConfig config, UnitTeamType casterTeam = UnitTeamType.TeamA,
        bool resetSceneView = true)
    {
        ReleaseTimelineControl();
        var next = new TimelineControl();
        try
        {
            next.Init(this, config, casterTeam, resetSceneView);
            timelineControl = next;
        }
        catch
        {
            next.OnDestroy();
            throw;
        }
    }

    /// <summary>界面与 MCP 新建共用的3D模板：施法对象、效果绑定、Animator 依次连线。</summary>
    internal static TimelineConfig CreateDefaultTimelineConfig(string timelineId = "new_timeline")
    {
        var factory = new TimelineControl();
        var castTargets = factory.CreateEmptyClip(TimelineClipType.SetCastTargets).Config;
        var hitTargets = factory.CreateEmptyClip(TimelineClipType.SetHitTargets).Config;
        hitTargets.PreKey = castTargets.ClipKey;
        return new TimelineConfig
        {
            TimelineId = timelineId,
            PreviewCasterAddress = EditorTimelineAssetPathUtil.DefaultUnitLogicalName,
            DurationBindClipKey = "",
            Clips = new List<TimelineClipConfigBase>
            {
                castTargets,
                hitTargets,
                new TimelineClipPlayAnimatorConfig
                {
                    ClipKey = TimelineKeys.GenerateClipKey(),
                    PreKey = hitTargets.ClipKey,
                    Type = TimelineClipType.PlayAnimator,
                    PrefabPath = EditorTimelineAssetPathUtil.DefaultUnitLogicalName,
                    ParamName = "Attack",
                    AnimatorStateSpeed = 1f,
                    StartTime = 0,
                    Duration = 0,
                },
            },
        };
    }

    private void OnNewTimeline()
    {
        if (!TryConfirmDiscardOrSaveUnsavedChanges())
        {
            return;
        }

        if (!EditorUtility.DisplayDialog("新建时间轴", "新建将清空当前编辑内容，是否继续？", "确定", "取消"))
        {
            return;
        }

        timelineControl?.OnDestroy();
        timelineControl = null;
        currentTimelineFilePath = null;
        CreateTimelineControl(CreateDefaultTimelineConfig());
        MarkNewTimelineUnsaved();
        StopPreView();
        Repaint();
    }

    private void OnLoadTimeline()
    {
        if (!TryConfirmDiscardOrSaveUnsavedChanges())
        {
            return;
        }

        var path = EditorUtility.OpenFilePanel("加载时间轴", DefaultTimelineFolder, "json");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (TryLoadTimelineFromPath(path, true))
        {
            PersistLastTimelinePath();
            StopPreView();
            Repaint();
        }
    }

    private bool TryLoadTimelineFromPath(string path, bool showErrorDialog)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = TimelineJsonUtil.FromJson(json);
            if (config == null)
            {
                if (showErrorDialog)
                {
                    EditorUtility.DisplayDialog("加载失败", "JSON 解析失败。", "确定");
                }

                return false;
            }

            CreateTimelineControl(config);
            currentTimelineFilePath = path;
            CaptureSavedSnapshot();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (showErrorDialog)
            {
                EditorUtility.DisplayDialog("加载失败", $"{e.GetType().Name}: {e.Message}", "确定");
            }

            return false;
        }
    }

    private static string GetLastTimelinePath()
    {
        var projectKey = LastTimelinePathPrefsKey + "." + Application.dataPath;
        if (EditorPrefs.HasKey(projectKey))
        {
            return EditorPrefs.GetString(projectKey);
        }
        var legacy = EditorPrefs.GetString(LastTimelinePathPrefsKey, string.Empty);
        var projectRoot = Path.GetDirectoryName(Application.dataPath) + Path.DirectorySeparatorChar;
        return !string.IsNullOrEmpty(legacy) && Path.GetFullPath(legacy).StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
            ? legacy
            : string.Empty;
    }

    private void PersistLastTimelinePath()
    {
        if (string.IsNullOrEmpty(currentTimelineFilePath))
        {
            return;
        }

        EditorPrefs.SetString(LastTimelinePathPrefsKey + "." + Application.dataPath, currentTimelineFilePath);
    }

    /// <summary>工具栏与 Ctrl+S：覆盖当前文件，没有路径则另存为。</summary>
    public void SaveCurrentTimeline()
    {
        TrySaveCurrentTimeline(true);
    }

    /// <summary>
    /// 覆盖当前路径；尚无路径时走另存为。成功后更新未保存基线。
    /// </summary>
    private bool TrySaveCurrentTimeline(bool showSuccessDialog)
    {
        if (timelineControl == null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(currentTimelineFilePath))
        {
            return TrySaveTimelineAs(showSuccessDialog);
        }

        if (!TrySaveTimelineFromGui(currentTimelineFilePath, showSuccessDialog, false))
        {
            return false;
        }

        CaptureSavedSnapshot();
        Repaint();
        return true;
    }

    /// <summary>弹出另存为面板并写盘。取消或失败返回 false。</summary>
    private bool TrySaveTimelineAs(bool showSuccessDialog)
    {
        if (timelineControl == null)
        {
            return false;
        }

        var defaultName = GetDefaultSaveFileName();
        var path = EditorUtility.SaveFilePanel("另存为", DefaultTimelineFolder, defaultName, "json");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var previousId = timelineControl.Config.TimelineId;
        ApplyTimelineIdFromFilePath(path);
        if (!TrySaveTimelineFromGui(path, showSuccessDialog, true))
        {
            timelineControl.Config.TimelineId = previousId;
            return false;
        }

        currentTimelineFilePath = path;
        PersistLastTimelinePath();
        CaptureSavedSnapshot();
        Repaint();
        return true;
    }

    private bool TrySaveTimelineFromGui(string path, bool showSuccessDialog, bool overwrite)
    {
        try
        {
            SaveTimelineForMcp(path, overwrite);
            if (showSuccessDialog)
            {
                EditorUtility.DisplayDialog(TimelineBindingEditorText.SaveSucceeded, path, TimelineBindingEditorText.Confirm);
            }
            return true;
        }
        catch (InvalidOperationException) when (!overwrite)
        {
            if (EditorUtility.DisplayDialog(TimelineBindingEditorText.SaveFailed,
                TimelineBindingEditorText.OverwriteConflict, TimelineBindingEditorText.Confirm, TimelineBindingEditorText.Cancel))
            {
                return TrySaveTimelineFromGui(path, showSuccessDialog, true);
            }
            return false;
        }
        catch (Exception error)
        {
            EditorUtility.DisplayDialog(TimelineBindingEditorText.SaveFailed, error.Message, TimelineBindingEditorText.Confirm);
            return false;
        }
    }

    private void ApplyTimelineIdFromFilePath(string path)
    {
        if (timelineControl?.Config == null)
        {
            return;
        }

        timelineControl.Config.TimelineId = Path.GetFileNameWithoutExtension(path);
    }

    private string ResolveSavePathByTimelineId()
    {
        var directory = DefaultTimelineFolder;
        if (!string.IsNullOrEmpty(currentTimelineFilePath))
        {
            var dir = Path.GetDirectoryName(currentTimelineFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                directory = dir;
            }
        }

        var fileName = SanitizeTimelineIdForFileName(timelineControl.Config?.TimelineId) + ".json";
        return Path.Combine(directory, fileName);
    }

    private static string SanitizeTimelineIdForFileName(string timelineId)
    {
        if (string.IsNullOrWhiteSpace(timelineId))
        {
            return "timeline";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = timelineId.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private bool WriteTimelineToFile(string path, bool showSuccessDialog, bool atomic = true)
    {
        timelineControl?.OnStopPreview();

        if (!timelineControl.BuildConfigForSave(null))
        {
            return false;
        }

        TimelineEditorMcpValidation.RequireEditor(timelineControl.Config, timelineControl);
        var config = timelineControl.Config;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = TimelineJsonUtil.ToJson(config, true);
        if (atomic)
        {
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                File.Delete(tempPath);
            }
        }
        else
        {
            File.WriteAllText(path, json);
        }
        AssetDatabase.Refresh();
        if (showSuccessDialog)
        {
            EditorUtility.DisplayDialog("保存成功", path, "确定");
        }

        return true;
    }

    private string GetDefaultSaveFileName()
    {
        return SanitizeTimelineIdForFileName(timelineControl.Config?.TimelineId) + ".json";
    }

    /// <summary>
    /// 新建或加载前询问未保存改动。保存失败或点取消则中止；点不保存则继续，内容被替换前仍保持脏标记。
    /// </summary>
    private bool TryConfirmDiscardOrSaveUnsavedChanges()
    {
        RefreshUnsavedChangesFlag();
        if (!hasUnsavedChanges)
        {
            return true;
        }

        var choice = EditorUtility.DisplayDialogComplex(
            "尚未保存",
            "当前技能尚未保存。是否保存？",
            "保存",
            "取消",
            "不保存");
        if (choice == 1)
        {
            return false;
        }

        if (choice == 2)
        {
            return true;
        }

        return TrySaveCurrentTimeline(false);
    }

    /// <summary>把当前可保存 JSON 记为基线，并清掉未保存标记。</summary>
    private void CaptureSavedSnapshot()
    {
        newTimelineNeverSaved = false;
        savedTimelineFileContents = File.Exists(currentTimelineFilePath)
            ? File.ReadAllText(currentTimelineFilePath)
            : null;
        if (TrySerializeCurrentTimeline(out var json))
        {
            savedTimelineJsonSnapshot = json;
        }

        hasUnsavedChanges = false;
        RefreshWindowTitle();
    }

    /// <summary>新建且从未写入文件，关闭时必须询问是否保存。</summary>
    private void MarkNewTimelineUnsaved()
    {
        newTimelineNeverSaved = true;
        savedTimelineJsonSnapshot = null;
        hasUnsavedChanges = true;
        RefreshWindowTitle();
    }

    /// <summary>按当前可保存 JSON 对比基线，更新未保存标记与窗口标题。</summary>
    public void RefreshUnsavedChangesFlag()
    {
        if (timelineControl == null)
        {
            return;
        }

        bool dirty;
        if (newTimelineNeverSaved)
        {
            dirty = true;
        }
        else if (!TrySerializeCurrentTimeline(out var json))
        {
            return;
        }
        else
        {
            dirty = !string.Equals(json, savedTimelineJsonSnapshot, StringComparison.Ordinal);
        }

        var title = BuildWindowTitle();
        if (hasUnsavedChanges == dirty && titleContent != null && titleContent.text == title)
        {
            return;
        }

        hasUnsavedChanges = dirty;
        titleContent = new GUIContent(title);
    }

    private void RefreshUnsavedChangesIfUserEdited()
    {
        var evt = Event.current;
        if (evt == null)
        {
            return;
        }

        if (evt.type != EventType.MouseUp && evt.type != EventType.KeyUp)
        {
            return;
        }

        RefreshUnsavedChangesFlag();
    }

    private void RefreshWindowTitle()
    {
        titleContent = new GUIContent(BuildWindowTitle());
    }

    /// <summary>Dock 标题不含额外星号；未保存时由 <c>hasUnsavedChanges</c> 追加一个 *。</summary>
    private string BuildWindowTitle()
    {
        var id = timelineControl?.Config?.TimelineId;
        if (string.IsNullOrEmpty(id))
        {
            id = "未命名";
        }

        return "技能编辑器 - " + id;
    }

    private bool TrySerializeCurrentTimeline(out string json)
    {
        json = null;
        if (timelineControl == null || timelineControl.Config == null)
        {
            return false;
        }

        if (!timelineControl.BuildConfigForSave(null))
        {
            return false;
        }

        json = TimelineJsonUtil.ToJson(timelineControl.Config, true);
        return true;
    }

    #endregion

    #region 预览播放控制

    public void PlayPreview()
    {
        IsPlaying = true;
    }

    public void PausePreView()
    {
        IsPlaying = false;
    }

    public void StopPreView(bool lerp = false)
    {
        PausePreView();
        SetRunningTime(-0.1f, lerp);
        timelineControl?.OnStopPreview();
    }

    /// <summary>播放头是否正在按标尺点击逻辑滑向目标。</summary>
    public bool IsPlayheadLerping => lerpSetRunningTimeEndValue;

    /// <summary>
    /// 先停播放，再按点时间轴标尺的逻辑把播放头滑到目标时间（不瞬移）。
    /// Clip 进出依赖连续过边界，MCP 定点采样必须走这条。
    /// </summary>
    public void SeekPlayheadLikeRulerClick(double time)
    {
        PausePreView();
        if (time < 0)
        {
            time = 0;
        }

        SetRunningTime(time, lerp: true);
        Repaint();
    }

    /// <summary>立即设置播放头（无 lerp），仅录制/从开头播放的起点使用。</summary>
    public void SeekRunningTimeImmediate(double time)
    {
        lerpSetRunningTimeEndValue = false;
        RunningTime = time;
        Repaint();
        SceneView.RepaintAll();
    }

    /// <summary>
    /// 从头开始播放并录制。untilCutoff=true 时播到 min(CutOff, 内容终点) 自动结束并回调；
    /// 否则需 Stop / 取消录制按钮结束。
    /// </summary>
    public void StartRecordingPlay(float sampleHz, bool untilCutoff, Action<string, object> onCompleted)
    {
        if (timelineControl == null)
        {
            return;
        }

        if (IsRecording)
        {
            previewRecorder.Cancel();
        }

        pendingRecordCompleted = onCompleted;
        StopPreView();
        SeekRunningTimeImmediate(0);
        previewRecorder.Begin(
            timelineControl,
            sampleHz,
            untilCutoff,
            (json, payload) =>
            {
                var cb = pendingRecordCompleted;
                pendingRecordCompleted = null;
                if (!string.IsNullOrEmpty(json))
                {
                    EditorGUIUtility.systemCopyBuffer = json;
                }

                cb?.Invoke(json, payload);
            });
        PlayPreview();
        Repaint();
    }

    /// <summary>结束录制并返回 JSON；未在录制时返回 null。</summary>
    public string FinishRecording(string stopReason)
    {
        if (!IsRecording)
        {
            return null;
        }

        return previewRecorder.Finish(stopReason);
    }

    /// <summary>MCP / 自动化：按路径或 TimelineId 加载技能。</summary>
    public bool TryLoadTimelineForMcp(string pathOrTimelineId, out string error)
    {
        return TryLoadTimelineForMcp(pathOrTimelineId, false, out error);
    }

    internal bool TryLoadTimelineForMcp(string pathOrTimelineId, bool discardChanges, out string error)
    {
        error = null;
        if (IsRecording || IsPlaying || IsPlayheadLerping)
        {
            error = "请先停止预览或等待定位结束。";
            return false;
        }
        RefreshUnsavedChangesFlag();
        if (hasUnsavedChanges && !discardChanges)
        {
            error = "当前技能有未保存改动。请先保存，或显式指定 discardChanges。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(pathOrTimelineId))
        {
            error = "路径或 TimelineId 为空。";
            return false;
        }

        var path = ResolveTimelinePath(pathOrTimelineId.Trim());
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            error = "找不到技能 JSON: " + pathOrTimelineId;
            return false;
        }

        if (!TryLoadTimelineFromPath(path, false))
        {
            error = "解析或加载失败: " + path;
            return false;
        }

        PersistLastTimelinePath();
        StopPreView();
        Repaint();
        return true;
    }

    /// <summary>替换编辑缓冲区。批量操作提交后才重建预览。</summary>
    internal void ReplaceTimelineForMcp(TimelineConfig config, bool newDocument)
    {
        StopPreView();
        if (newDocument)
        {
            CreateTimelineControl(config);
            currentTimelineFilePath = null;
            MarkNewTimelineUnsaved();
        }
        else
        {
            timelineControl.Reload(config);
            RefreshUnsavedChangesFlag();
        }
        Repaint();
    }

    /// <summary>通过同一保存管线写入技能文件，并更新窗口基线。</summary>
    internal void SaveTimelineForMcp(string path, bool allowOverwrite)
    {
        var project = Path.GetDirectoryName(Application.dataPath);
        var current = string.IsNullOrEmpty(currentTimelineFilePath)
            ? null
            : Path.GetFullPath(currentTimelineFilePath);
        var destination = string.IsNullOrWhiteSpace(path)
            ? current
            : Path.GetFullPath(Path.Combine(project, path));
        var root = Path.GetFullPath(Path.Combine(project, "Assets/Package_Unit/Timeline")) + Path.DirectorySeparatorChar;
        if (destination is null || !destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || !destination.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("请指定 Assets/Package_Unit/Timeline 下的 JSON 路径。");
        }
        var inPlace = string.Equals(destination, current, StringComparison.OrdinalIgnoreCase);
        if (inPlace && !allowOverwrite && (!File.Exists(destination)
            || !string.Equals(savedTimelineFileContents, File.ReadAllText(destination), StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("当前文件自加载后已在磁盘上变更。请另存为或显式指定 overwrite。");
        }
        if (File.Exists(destination) && !allowOverwrite && !inPlace)
        {
            throw new IOException("目标文件已存在。请另选路径或显式指定 overwrite。");
        }
        if (!WriteTimelineToFile(destination, false, true))
        {
            throw new IOException("编辑器未能序列化当前技能。");
        }
        currentTimelineFilePath = destination;
        PersistLastTimelinePath();
        CaptureSavedSnapshot();
    }

    private string savedTimelineFileContents;

    internal static string ResolveTimelinePath(string pathOrTimelineId)
    {
        if (pathOrTimelineId.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || pathOrTimelineId.Contains("/")
            || pathOrTimelineId.Contains("\\"))
        {
            if (File.Exists(pathOrTimelineId))
            {
                return pathOrTimelineId;
            }

            var projectRelative = pathOrTimelineId.Replace('\\', '/');
            if (projectRelative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRelative));
                if (File.Exists(abs))
                {
                    return abs;
                }
            }

            return pathOrTimelineId;
        }

        var id = pathOrTimelineId;
        if (id.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            id = Path.GetFileNameWithoutExtension(id);
        }

        var underBase = Path.Combine(Application.dataPath, "Package_Unit/Timeline/Base", id + ".json");
        if (File.Exists(underBase))
        {
            return underBase;
        }

        var guids = AssetDatabase.FindAssets(id + " t:TextAsset");
        for (var i = 0; i < guids.Length; i++)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (assetPath.EndsWith("/" + id + ".json", StringComparison.OrdinalIgnoreCase)
                || assetPath.EndsWith("\\" + id + ".json", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            }
        }

        return underBase;
    }

    #endregion

    #region SetRunningTime

    private float setRunningTimeSpeed = 7;
    private double setRunningTimeEndValue = 0;
    private bool lerpSetRunningTimeEndValue = false;

    /// <summary>设置播放头时间；lerp=true 时逐帧逼近目标（用于 ruler 拖拽）。</summary>
    protected override void SetRunningTime(double runningTime, bool lerp = false)
    {
        if (lerp)
        {
            lerpSetRunningTimeEndValue = true;
            setRunningTimeEndValue = runningTime;
        }
        else
        {
            lerpSetRunningTimeEndValue = false;
            this.RunningTime = runningTime;
        }
    }

    private void SetRunningTimeLogic()
    {
        if (!lerpSetRunningTimeEndValue)
        {
            return;
        }

        float elapsed = (float)EditorApplication.timeSinceStartup - _lastUpdateTime;
        var cur = (float)this.RunningTime;
        var target = (float)setRunningTimeEndValue;
        var next = Mathf.MoveTowards(cur, target, setRunningTimeSpeed * elapsed);

        RunningTime = next;
        if (FloatEqual(next, target))
        {
            lerpSetRunningTimeEndValue = false;
        }
    }

    bool FloatEqual(float a, float b, float epsilon = 1e-6f)
    {
        return Math.Abs(a - b) < epsilon;
    }

    #endregion

    #region 数学与辅助

    /// <summary>
    /// 获取区间 [min, max] 内所有形如 c + k*n 的值，并取整为最接近的整数
    /// </summary>
    /// <param name="c">常量偏移（float）</param>
    /// <param name="n">步长（int，不能为0）</param>
    /// <param name="min">区间最小值（包含）</param>
    /// <param name="max">区间最大值（包含）</param>
    /// <returns>所有满足条件的整数值 y 列表</returns>
    public List<int> GetSequenceInRange(float c, int n, float min, float max)
    {
        List<int> result = new List<int>();

        if (n == 0)
        {
            Debug.LogError("n 不能为 0");
            return result;
        }

        // 确保 n 为正数以便计算
        int step = n > 0 ? n : -n;

        // 找到第一个 k 使得 c + k * step >= min
        int firstK = Mathf.CeilToInt((min - c) / step);
        // 找到最后一个 k 使得 c + k * step <= max
        int lastK = Mathf.FloorToInt((max - c) / step);

        for (int k = firstK; k <= lastK; k++)
        {
            float value = c + k * step;
            int y = Mathf.RoundToInt(value); // 四舍五入取整
            result.Add(y);
        }

        return result;
    }

    public static int GetPreviousMultiple(float value, int n)
    {
        if (n == 0)
        {
            Debug.LogError("n 不能为 0");
            return 0;
        }

        if (n < 0) n = -n;

        // 直接计算：向下取整，如果正好整除则减 1
        int k = (int)Mathf.Floor(value / n - 1e-6f); // 减去微小量避免边界情况
        return k * n;
    }

    /// <summary>
    /// 获取最接近 x 的形如 c + k*n 的整数 y
    /// </summary>
    /// <param name="c">常量（float）</param>
    /// <param name="n">倍数步长（int，不可为0）</param>
    /// <param name="x">目标值（float）</param>
    /// <returns>整数 y</returns>
    public static int GetClosestMultiple(float c, int n, float x)
    {
        if (n == 0)
        {
            Debug.LogWarning("n 为 0，无法形成倍数序列，返回最接近 c 的整数。");
            return Mathf.RoundToInt(c);
        }

        // 计算 k = round((x - c) / n)
        float kFloat = (x - c) / n;
        int k = Mathf.RoundToInt(kFloat); // 四舍五入到整数

        // 计算 y = c + k*n
        float yFloat = c + k * n;

        // 最终结果取整（因为 y 理论上应为整数，但 float 计算可能有精度问题）
        return Mathf.RoundToInt(yFloat);
    }

    #endregion

    #region 遗留代码

    private void DrawCurveLine(float offsetX)
    {
        // draw curve
        //Keyframe[] keys = m_AnimationCurve.keys;
        //int keyCount = keys.Length;
        //if (keyCount > 1)
        //{
        //    //Keyframe startKey = keys[0];
        //    //Keyframe endKey = keys[keyCount - 1];
        //    m_cachePoints.Clear();
        //    m_segmentResolution = Mathf.Clamp(m_segmentResolution, 3, 50);
        //    for (int i = 0; i < keyCount - 1; i++)
        //    {
        //        Keyframe cur = keys[i];
        //        Keyframe next = keys[i + 1];

        //        m_cachePoints.Add(new Vector3(cur.time, cur.value));
        //        float num = Mathf.Lerp(cur.time, next.time, 0.001f / (float)m_segmentResolution);
        //        m_cachePoints.Add(new Vector3(num, m_AnimationCurve.Evaluate(num)));

        //        for (float num2 = 1f; num2 < (float)m_segmentResolution; num2 += 1f)
        //        {
        //            num = Mathf.Lerp(cur.time, next.time, num2 / (float)m_segmentResolution);
        //            m_cachePoints.Add(new Vector3(num, m_AnimationCurve.Evaluate(num)));
        //        }
        //        num = Mathf.Lerp(cur.time, next.time, 1f - 0.000f / (float)m_segmentResolution);
        //        m_cachePoints.Add(new Vector3(num, m_AnimationCurve.Evaluate(num)));
        //        m_cachePoints.Add(new Vector3(next.time, next.value));
        //    }

        //    for (int i = 0; i < m_cachePoints.Count; i++)
        //    {
        //        float time = m_cachePoints[i].x;
        //        float value = m_cachePoints[i].y;
        //        time = TimeToPixel((double)time) - offsetX;
        //        value = YToPixel(value);

        //        Vector2 pos = new Vector2(time, value);
        //        m_cachePoints[i] = pos;
        //    }
        //    if (m_cachePoints.Count != 0)
        //    {
        //        Handles.BeginGUI();
        //        Handles.color = Color.white;
        //        Handles.DrawAAPolyLine(3, m_cachePoints.ToArray());
        //        Handles.EndGUI();
        //    }

        //    keys = m_AnimationCurve.keys;
        //    for (int i = 0; i < keys.Length; i++)
        //    {
        //        float time = keys[i].time;
        //        float value = keys[i].value;
        //        int iconWidth = 12;
        //        value = YToPixel(value);
        //        time = TimeToPixel((double)time) - offsetX;
        //        Vector2 pos = new Vector2(time, value);

        //        Rect rect = new Rect(pos.x - iconWidth * 0.5f, pos.y - iconWidth * 0.5f, iconWidth, iconWidth);
        //        if (iconTexture == null)
        //            iconTexture = EditorGUIUtility.IconContent("Animation.Record", "|Enable/disable keyframe recording mode.").image;
        //        GUI.DrawTexture(rect, iconTexture);
        //    }
        //}
    }

    private List<Vector3> m_cachePoints = new List<Vector3>();
    public int m_segmentResolution = 20;
    private static Texture iconTexture = null;

    #endregion
}
