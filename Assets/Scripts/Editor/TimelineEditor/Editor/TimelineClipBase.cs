using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 时间轴 Clip 基类。负责 Rect 绘制、进出边界检测、预览状态机及 Inspector 通用字段。
/// </summary>
public class TimelineClipBase
{
    #region 进出边界追踪

    protected double lastRunningTime = -10;
    protected float lastRectX;
    private float lastPlayheadLocalX;
    private float lastContentRightLocalX;
    private bool inOutInitialized;
    private double locateFlashUntil;

    protected bool isFirstLeftIn = true;
    protected bool isFirstLeftOut = true;
    protected bool isFirstRightIn = true;
    protected bool isFirstRightOut = true;

    /// <summary>Clip 内容区内已播放时长（RunningTime - StartTime - DelayTime）。</summary>
    protected float playTiming;

    #endregion

    #region 绘制样式

    protected Rect rect = new Rect();
    public Rect Rect => rect;

    private Color fillColor = new Color(34f / 255f, 77f / 255f, 122f / 255f, 1f);
    private Color borderColor = new Color(1f, 0.92f, 0.35f, 1f);
    protected float borderWidth = 2f;
    protected float paddingRate = 0.1f;

    private static GUIStyle clipLabelStyle;
    private static readonly Color ClipLabelTextColor = new Color(0xA3 / 255f, 0xA3 / 255f, 0xA3 / 255f, 1f);

    public Color BorderColor
    {
        get => borderColor;
        set => borderColor = value;
    }

    protected float height => control.Timeline.GetItemPixelHeight() - control.Timeline.GetItemPixelHeight() * paddingRate;

    private readonly TimelineWaveBase delayWave = new TimelineWaveBase();

    #endregion

    #region 配置与实例

    protected string assetDataPath = "Assets/Package_Res/";

    protected TimelineControl control;
    protected TimelineClipConfigBase config;
    public TimelineClipConfigBase Config => config;

    protected float endTime => control != null && config != null
        ? control.ResolveClipContentEndTime(config)
        : config.StartTime + config.DelayTime + config.Duration;

    protected GameObject prefab;
    protected GameObject instance;
    public GameObject Instance => instance;
    protected string prefabPath;
    protected bool isBaseTransform;

    #endregion

    #region 初始化

    public virtual void PreInit(TimelineControl control, TimelineClipConfigBase config)
    {
    }

    public virtual void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        this.control = control;
        SetConfig(config);
        InitEvents();
        InitNodes();

        if (prefab == null && !string.IsNullOrEmpty(prefabPath))
        {
            prefab = LoadClipPrefab(prefabPath);
        }

        if (isBaseTransform)
        {
            instance = this.control.SetMyTransform(prefabPath).gameObject;
        }

        ChangeClipTimeStateType(ClipTimeStateType.OutTime);
        ChangeClipOperateStateType(ClipOperateStateType.Normal);
        inOutInitialized = false;
    }

    public void SetConfig(TimelineClipConfigBase config)
    {
        if (config == null)
        {
            return;
        }
        this.config = config;
    }

    #endregion

    #region Draw 主流程

    /// <summary>画布绘制与编排入口；播放边界和表现采样由 shared 播放器驱动。</summary>
    public virtual void Draw()
    {
        SetRect();

        playTiming = (float)this.control.Timeline.RunningTime - (config.StartTime + config.DelayTime);

        DrawRect();
        DrawDelayWave();
        DrawEvents();
        ClipOperateStateTypeLogic();
        DrawClipTypeLabel();
        AdjustStartTimeByPreClipKeyLogic();
        DrawNextClipLineLogic();
        DrawNodes();

        lastRunningTime = this.control.Timeline.RunningTime;
    }

    /// <summary>重新计算预览时长等派生数据，不绘制或触发业务通知。</summary>
    public virtual void PreparePreviewSample()
    {
    }

    /// <summary>清空边界追踪，供从演出初始状态重建预览。</summary>
    public void ResetPreviewSample()
    {
        lastRunningTime = -0.1;
        isFirstLeftIn = true;
        isFirstLeftOut = true;
        isFirstRightIn = true;
        isFirstRightOut = true;
        inOutInitialized = false;
    }

    /// <summary>只求值当前时刻，供参数编辑后的预览重建使用。</summary>
    public void SamplePreviewAtCurrentTime(double previousTime)
    {
        var time = control.Timeline.RunningTime;
        var start = GetContentTimelineStartTime();
        var end = GetContentTimelineEndTime();
        playTiming = (float)(time - start);
        if (previousTime < start && time >= end)
        {
            OnPass(true);
        }
        else
        {
            if (previousTime < start && time >= start)
            {
                OnLeftIn();
            }
            if (previousTime < end && time >= end)
            {
                OnRightOut();
            }
        }
        var active = time >= start && time < end;
        ChangeClipTimeStateType(active ? ClipTimeStateType.InTime : ClipTimeStateType.OutTime);
        if (active)
        {
            OnInTimeLogic();
        }
        // 与下一轮画布的边界检测对齐，避免重绘重复触发进入或施力。
        SetRect();
        lastRunningTime = time;
        lastRectX = rect.x;
        lastPlayheadLocalX = GetPlayheadLocalX();
        lastContentRightLocalX = GetContentRightLocalX();
        inOutInitialized = true;
    }

    /// <summary>仅供历史编辑器专用片段适配；进入和退出由 shared 调度器决定。</summary>
    public void SampleFromPlayer(float localTime)
    {
        playTiming = localTime;
        if (curClipTimeStateType != ClipTimeStateType.InTime)
        {
            OnLeftIn();
            ChangeClipTimeStateType(ClipTimeStateType.InTime);
        }
        OnInTimeLogic();
    }

    /// <summary>共享调度器跨过片段结束边界时退出。</summary>
    public void ExitFromPlayer()
    {
        OnRightOut();
        ChangeClipTimeStateType(ClipTimeStateType.OutTime);
    }

    protected virtual void SetRect()
    {
        var y = this.control.Timeline.GetYByIndex(this.control.Clips.IndexOf(this));
        rect.x = control.Timeline.WorldPixelXToLocal(control.Timeline.TimeToPixel(config.StartTime + config.DelayTime));
        rect.y = control.Timeline.WorldPixelYToLocal(control.Timeline.YToPixel(y)) + paddingRate / 2.0f * control.Timeline.GetItemPixelHeight();
        var contentStartTime = GetContentTimelineStartTime();
        var contentEndTime = GetContentTimelineEndTime();
        rect.width = control.Timeline.TimeToPixel(contentEndTime) - control.Timeline.TimeToPixel(contentStartTime);
        rect.height = height;
    }

    protected virtual void DrawRect()
    {
        EditorGUI.DrawRect(rect, fillColor);
        var remaining = locateFlashUntil - EditorApplication.timeSinceStartup;
        if (remaining > 0d)
        {
            var alpha = (0.25f + 0.4f * Mathf.Abs(Mathf.Sin((float)remaining * 9f))) * Mathf.Min(1f, (float)remaining);
            EditorGUI.DrawRect(rect, new Color(1f, 0.85f, 0.2f, alpha));
            DrawBorder(rect, new Color(1f, 0.85f, 0.2f, 1f), 3f);
            control.Timeline.Repaint();
        }
    }

    /// <summary>更新片段布局，返回用于画布定位的当前矩形。</summary>
    public Rect GetLocateRect()
    {
        SetRect();
        return rect;
    }

    /// <summary>短暂闪烁提示片段，不改变选中和预览状态。</summary>
    public void FlashForLocate()
    {
        locateFlashUntil = EditorApplication.timeSinceStartup + 1.2d;
        control.Timeline.Repaint();
    }

    private void DrawClipTypeLabel()
    {
        if (config == null || rect.width <= 1f || rect.height <= 1f)
        {
            return;
        }

        if (clipLabelStyle == null)
        {
            clipLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.UpperCenter,
                clipping = TextClipping.Clip,
                wordWrap = false,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
            };
            ApplyClipLabelTextColor(clipLabelStyle);
        }

        const float labelPadding = 2f;
        float labelHeight = clipLabelStyle.lineHeight;
        var clipEvents = GetAllEvents();
        var hasEvents = clipEvents != null && clipEvents.Count > 0;
        clipLabelStyle.alignment = hasEvents ? TextAnchor.UpperCenter : TextAnchor.MiddleCenter;
        var labelRect = hasEvents
            ? new Rect(rect.x, rect.y + labelPadding, rect.width, labelHeight)
            : new Rect(rect.x, rect.y, rect.width, rect.height);
        var label = UnitFeatureTypeDisplay.GetClipLabelName(config);
        GUI.Label(labelRect, label, clipLabelStyle);
    }

    private static void ApplyClipLabelTextColor(GUIStyle style)
    {
        style.normal.textColor = ClipLabelTextColor;
        style.hover.textColor = ClipLabelTextColor;
        style.active.textColor = ClipLabelTextColor;
        style.focused.textColor = ClipLabelTextColor;
        style.onNormal.textColor = ClipLabelTextColor;
        style.onHover.textColor = ClipLabelTextColor;
        style.onActive.textColor = ClipLabelTextColor;
        style.onFocused.textColor = ClipLabelTextColor;
    }

    /// <summary>绘制 DelayTime 区间的波浪线。</summary>
    private void DrawDelayWave()
    {
        if (config == null || config.DelayTime <= 0f)
        {
            return;
        }

        var waveY = rect.y + rect.height * 0.5f;
        var startX = control.Timeline.WorldPixelXToLocal(control.Timeline.TimeToPixel(config.StartTime));
        var endX = control.Timeline.WorldPixelXToLocal(control.Timeline.TimeToPixel(config.StartTime + config.DelayTime));
        if (endX <= startX + 0.5f)
        {
            return;
        }

        delayWave.Draw(new Vector2(startX, waveY), new Vector2(endX, waveY));
    }

    protected virtual void DrawBorder()
    {
        DrawBorder(rect, borderColor, borderWidth);
    }

    protected void DrawBorder(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    #endregion

    #region Inspector

    #region EditorUpdate
    protected float editorTiming;
    public void EditorUpdateLogic(float deltaTime)
    {
        if(InTime())
        {
            editorTiming += deltaTime;
            OnEditorUpdate(deltaTime);
        }
        else{
            editorTiming = 0;
        }
    }
    public virtual void OnEditorUpdate(float deltaTime)
    {
    }
    #endregion


    public virtual void OnDrawGizmos()
    {
    }

    /// <summary>选中 Clip 时在 Scene 视图中绘制可交互 Handles（由技能编辑器转发 SceneView.duringSceneGui）。</summary>
    public virtual void OnSceneGUI(SceneView sceneView)
    {
    }

    public virtual void DrawInspector()
    {
        if (!TimelinePlayback.Supports(config))
        {
            EditorGUILayout.HelpBox(TimelineBindingEditorText.PreviewOnlyHelp, MessageType.Warning);
        }
        if (config.Type == TimelineClipType.PlayHit)
        {
            EditorGUILayout.HelpBox(TimelineBindingEditorText.HitPointHelp, MessageType.Info);
        }
        var clipName = config != null ? UnitFeatureTypeDisplay.GetClipLabelName(config) : "Clip";
        EditorGUILayout.LabelField($"{clipName}参数配置", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("类型", UnitFeatureTypeDisplay.GetDisplayName(config.Type));
        EditorGUILayout.TextField("ClipKey", config.ClipKey ?? string.Empty);
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(this.config.PreKey));
        config.StartTime = EditorGUILayout.FloatField("开始时间", config.StartTime);
        EditorGUI.EndDisabledGroup();

        config.DelayTime = EditorGUILayout.FloatField("延迟时间", config.DelayTime);

        control.SyncClipDurationFromCatch(config);
        EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(config.EndByOtherClipEnd));
        config.Duration = EditorGUILayout.FloatField("时长", config.Duration);
        EditorGUI.EndDisabledGroup();
        if (!string.IsNullOrEmpty(config.EndByOtherClipEnd))
        {
            EditorGUILayout.HelpBox("时长随 Catch 的 Clip 动态计算", MessageType.None);
        }

        DrawEndByOtherClipEndInspector();

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("前置Key", config.PreKey ?? string.Empty);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(4);
        EditorGUILayout.Space(4);
    }

    /// <summary>
    /// 绘制「是否受定帧影响」勾选；仅对会参与 HitStop 时间映射的 Clip 调用。
    /// </summary>
    /// <param name="affectedByHitStop">当前配置值（默认应为 true）。</param>
    /// <returns>勾选变更后的值。</returns>
    protected bool DrawAffectedByHitStopToggle(bool affectedByHitStop)
    {
        EditorGUI.BeginChangeCheck();
        var next = EditorGUILayout.Toggle("是否受定帧影响", affectedByHitStop);
        if (EditorGUI.EndChangeCheck())
        {
            control.Timeline.StopPreView();
            control.Timeline.Repaint();
        }

        return next;
    }

    private void DrawEndByOtherClipEndInspector()
    {
        if (control.IsWaitingEndByOtherClipCatch(this))
        {
            EditorGUILayout.HelpBox("Catch 模式：请在时间轴点击选择要绑定的 Clip", MessageType.Warning);
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("EndByOtherClipEnd", config.EndByOtherClipEnd ?? string.Empty);
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Catch", GUILayout.Width(64)))
        {
            control.BeginEndByOtherClipCatch(this);
        }
        EditorGUILayout.EndHorizontal();
    }

    #endregion

    #region 时长快捷键

    /// <summary>
    /// Ctrl+←/→ 按帧调整本 Clip 的可编辑时长。
    /// 返回 false 表示时长由 Catch / 资源 / 瞬时逻辑锁定，无法手调。
    /// </summary>
    public virtual bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        if (config == null || frameDelta == 0 || frameStep <= 0f)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(config.EndByOtherClipEnd))
        {
            return false;
        }

        config.Duration = SnapNudgeDuration(config.Duration, frameStep, frameDelta);
        return true;
    }

    /// <summary>按帧步进并吸附到帧边界，最短不少于 1 帧。</summary>
    /// <remarks>
    /// 必须在「帧序号」整数上加减。若先算 current+frameStep 再 SnapToFrame，
    /// 在整秒边界（如 1.0）会因浮点误差 Floor 回原帧，表现为 Ctrl+→ 加不长。
    /// </remarks>
    protected float SnapNudgeDuration(float current, float frameStep, int frameDelta)
    {
        var rate = control != null && control.Timeline != null ? control.Timeline._frameRate : 0f;
        if (rate <= 0f)
        {
            return Mathf.Max(frameStep, current + frameStep * frameDelta);
        }

        var frame = Mathf.RoundToInt(current * rate);
        frame = Mathf.Max(1, frame + frameDelta);
        return frame / rate;
    }

    /// <summary>与 <see cref="SnapNudgeDuration"/> 相同，但允许减到 0（逻辑时长可为 0 的 Clip）。</summary>
    protected float SnapNudgeDurationAllowZero(float current, float frameStep, int frameDelta)
    {
        var rate = control != null && control.Timeline != null ? control.Timeline._frameRate : 0f;
        if (rate <= 0f)
        {
            var next = current + frameStep * frameDelta;
            return next <= 0f ? 0f : Mathf.Max(frameStep, next);
        }

        var frame = Mathf.RoundToInt(current * rate);
        frame = frame + frameDelta;
        if (frame <= 0)
        {
            return 0f;
        }

        return frame / rate;
    }

    #endregion

    #region 时间查询

    public float GetTimelineStartTime()
    {
        return config != null ? config.StartTime : 0f;
    }

    /// <summary>Clip 内容区在时间轴上的起点（StartTime + DelayTime），事件时间相对此偏移。</summary>
    public float GetContentTimelineStartTime()
    {
        return config.StartTime + config.DelayTime;
    }

    public float GetContentTimelineEndTime()
    {
        return endTime;
    }

    public bool InTime()
    {
        return this.control.Timeline.RunningTime >= GetContentTimelineStartTime()
            && this.control.Timeline.RunningTime <= this.endTime;
    }

    public bool IsOverTime => this.control.Timeline.RunningTime > this.endTime;

    /// <summary>
    /// 鼠标在 Clip 时间轴上的横向比例：左端 0，右端 1（按 Duration 与鼠标像素时间换算）。
    /// </summary>
    public float GetOffsetXByMouse(Vector2 mousePosition)
    {
        if (control == null || config == null)
        {
            return 0f;
        }

        var effectiveDuration = control.ResolveClipDuration(config);
        if (effectiveDuration <= 0f)
        {
            return 0f;
        }

        var time = control.Timeline.PixelToTime(mousePosition.x);
        return Mathf.Clamp01((time - GetContentTimelineStartTime()) / effectiveDuration);
    }

    #endregion

    #region InOutLogic

    /// <summary>
    /// 检测播放头相对 Clip 的进出边界（时间轴坐标 + 像素坐标双判定），触发对应回调。
    /// 快速拖过整段 Clip 时走 <see cref="OnPass"/>。
    /// </summary>
    private void InOutLogic()
    {
        if (this.control.Timeline.RunningTime == -1)
        {
            inOutInitialized = false;
            return;
        }

        var runningTime = this.control.Timeline.RunningTime;
        var contentStartTime = GetContentTimelineStartTime();
        var contentEndTime = GetContentTimelineEndTime();
        var playheadLocalX = GetPlayheadLocalX();
        var contentLeftLocalX = rect.x;
        var contentRightLocalX = GetContentRightLocalX();

        if (!inOutInitialized)
        {
            inOutInitialized = true;
            lastRectX = contentLeftLocalX;
            lastPlayheadLocalX = playheadLocalX;
            lastContentRightLocalX = contentRightLocalX;
            return;
        }

        // 整段掠过：左→右 或 右→左
        if ((lastRunningTime < GetContentTimelineStartTime() && this.control.Timeline.RunningTime >= GetContentTimelineEndTime())
            || (lastPlayheadLocalX < lastRectX && playheadLocalX >= contentRightLocalX))
        {
            OnPass(true);
        }
        else if ((lastRunningTime > GetContentTimelineEndTime() && this.control.Timeline.RunningTime <= GetContentTimelineStartTime())
            || (lastPlayheadLocalX > lastContentRightLocalX && playheadLocalX <= contentRightLocalX))
        {
            OnPass(false);
        }

        var crossedLeftInByTime = lastRunningTime < contentStartTime
            && runningTime >= contentStartTime
            && runningTime < contentEndTime;
        var crossedLeftInByRect = lastPlayheadLocalX < lastRectX
            && playheadLocalX >= contentLeftLocalX
            && playheadLocalX <= contentRightLocalX;
        if (crossedLeftInByTime || crossedLeftInByRect)
        {
            OnLeftIn();
            isFirstLeftIn = false;
        }

        var crossedRightOutByTime = lastRunningTime <= contentEndTime
            && runningTime > contentEndTime
            && lastRunningTime >= contentStartTime;
        var wasInsideByRect = lastPlayheadLocalX >= lastRectX && lastPlayheadLocalX <= lastContentRightLocalX;
        var isAfterRightByRect = playheadLocalX > contentRightLocalX;
        if (crossedRightOutByTime || (wasInsideByRect && isAfterRightByRect))
        {
            OnRightOut();
            isFirstRightOut = false;
        }

        var crossedLeftOutByTime = lastRunningTime >= contentStartTime
            && runningTime < contentStartTime
            && lastRunningTime <= contentEndTime;
        var isBeforeLeftByRect = playheadLocalX < contentLeftLocalX;
        if (crossedLeftOutByTime || (wasInsideByRect && isBeforeLeftByRect))
        {
            OnLeftOut();
            isFirstLeftOut = false;
        }

        var crossedRightInByTime = lastRunningTime > contentEndTime
            && runningTime <= contentEndTime
            && runningTime >= contentStartTime;
        var wasAfterRightByRect = lastPlayheadLocalX > lastContentRightLocalX;
        var isInsideByRect = playheadLocalX >= contentLeftLocalX && playheadLocalX <= contentRightLocalX;
        if (crossedRightInByTime || (wasAfterRightByRect && isInsideByRect))
        {
            OnRightIn();
            isFirstRightIn = false;
        }

        lastRectX = contentLeftLocalX;
        lastPlayheadLocalX = playheadLocalX;
        lastContentRightLocalX = contentRightLocalX;
    }

    private float GetPlayheadLocalX()
    {
        return control.Timeline.WorldPixelXToLocal(control.Timeline.TimeToPixel(control.Timeline.RunningTime));
    }

    private float GetContentRightLocalX()
    {
        return control.Timeline.WorldPixelXToLocal(control.Timeline.TimeToPixel(GetContentTimelineEndTime()));
    }

    protected virtual void OnLeftIn()
    {
        this.playTiming = 0;
    }

    protected virtual void OnRightOut()
    {
        this.playTiming = config.Duration;
    }

    protected virtual void OnLeftOut()
    {
        this.playTiming = 0;
    }

    protected virtual void OnRightIn()
    {
        this.playTiming = config.Duration;
    }

    /// <summary>播放头一次性跳过整段 Clip 时，补发进出回调并修正 playTiming。</summary>
    protected virtual void OnPass(bool leftToRight)
    {
        if (leftToRight)
        {
            this.playTiming = config.Duration;
        }
        else
        {
            this.playTiming = 0;
        }

        if (leftToRight)
        {
            OnLeftIn();
            isFirstLeftIn = true;
            OnRightOut();
            isFirstRightOut = false;
        }
        else
        {
            OnRightIn();
            isFirstRightIn = false;
            OnLeftOut();
            isFirstLeftOut = false;
        }
    }

    #endregion

    #region ClipTimeStateType

    private ClipTimeStateType curClipTimeStateType;

    public enum ClipTimeStateType
    {
        OutTime,
        InTime,
    }

    private void ClipTimeStateTypeLogic()
    {
        switch (curClipTimeStateType)
        {
            case ClipTimeStateType.OutTime:
                if (InTime())
                {
                    ChangeClipTimeStateType(ClipTimeStateType.InTime);
                    return;
                }
                OnOutTimeLogic();
                break;
            case ClipTimeStateType.InTime:
                if (!InTime())
                {
                    ChangeClipTimeStateType(ClipTimeStateType.OutTime);
                    return;
                }
                OnInTimeLogic();
                break;
            default:
                break;
        }
    }

    public void ChangeClipTimeStateType(ClipTimeStateType type)
    {
        if (curClipTimeStateType == type)
        {
            return;
        }
        curClipTimeStateType = type;
        switch (curClipTimeStateType)
        {
            case ClipTimeStateType.OutTime:
                OnOutTime();
                break;
            case ClipTimeStateType.InTime:
                OnInTime();
                break;
            default:
                break;
        }
    }

    protected virtual void OnOutTime()
    {
        if (instance != null && !isBaseTransform)
        {
            GameObject.DestroyImmediate(instance);
            instance = null;
        }
    }

    protected virtual void OnOutTimeLogic()
    {
        //禁止做任何处理
    }

    protected virtual void OnInTime()
    {
        if (instance == null && prefab != null && !isBaseTransform)
        {
            instance = GameObject.Instantiate(prefab);
        }
    }

    protected virtual void OnInTimeLogic()
    {
        //禁止做任何处理
    }

    #endregion

    #region ClipOperateStateType

    private float dragPickMouseXOffset;
    private bool dragAdjustsDelayTime = true;

    private ClipOperateStateType curClipOperateStateType;
    public ClipOperateStateType CurClipOperateStateType => curClipOperateStateType;

    public enum ClipOperateStateType
    {
        Normal = 0,
        Selected,
        Drag,
    }

    private void ClipOperateStateTypeLogic()
    {
        switch (curClipOperateStateType)
        {
            case ClipOperateStateType.Normal:
                if (this.control.SelectClip == this)
                {
                    this.control.OnClipSelect(this);
                }
                break;
            case ClipOperateStateType.Selected:
                DrawBorder();
                if (control.MouseClickType == TimelineControl.MouseEventClickType.MouseRightUp && control.SelectClip == this)
                {
                    this.control.MenuControl.ShowClipMenu(this);
                    return;
                }

                if (
                    (this.control.MouseClickType == TimelineControl.MouseEventClickType.MouseLeftDown
                    || this.control.MouseClickType == TimelineControl.MouseEventClickType.MouseRightUp
                    )
                    && control.SelectClip != this)
                {
                    ChangeClipOperateStateType(ClipOperateStateType.Normal);
                    return;
                }

                var evt = Event.current;
                if (evt != null && (evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag) && evt.button == 0 && this.control.Contains(rect, evt.mousePosition))
                {
                    ChangeClipOperateStateType(ClipOperateStateType.Drag);
                    return;
                }

                if (this.control.SelectNothingInDrawRect)
                {
                    ChangeClipOperateStateType(ClipOperateStateType.Normal);
                    return;
                }
                break;
            case ClipOperateStateType.Drag:
                if (this.control.SelectNothingOutDrawRect)
                {
                    ChangeClipOperateStateType(ClipOperateStateType.Selected);
                    return;
                }

                if (this.control.MouseClickType == TimelineControl.MouseEventClickType.MouseLeftUp)
                {
                    ChangeClipOperateStateType(ClipOperateStateType.Selected);
                    return;
                }

                DrawBorder();
                ApplyClipDragFromMouseX(Event.current.mousePosition.x);
                break;
            default:
                break;
        }
    }

    public void ChangeClipOperateStateType(ClipOperateStateType type)
    {
        if (curClipOperateStateType == type)
        {
            return;
        }

        curClipOperateStateType = type;
        control.NotifySelectionChanged();

        switch (curClipOperateStateType)
        {
            case ClipOperateStateType.Normal:
                break;
            case ClipOperateStateType.Selected:
                if (this.control.SelectClip == this && this.control.MenuControl.IsEventConnecting)
                {
                    this.control.MenuControl.OnEventConnectClip(this);
                    return;
                }
                else if (this.control.SelectClip == this && this.control.MenuControl.IsClipConnecting)
                {
                    this.control.MenuControl.OnClipConnectClip(this);
                    return;
                }
                break;
            case ClipOperateStateType.Drag:
                BeginDragPickOffset(Event.current.mousePosition.x);
                break;
            default:
                break;
        }
    }

    /// <summary>有 PreKey 时拖拽调整 DelayTime，否则调整 StartTime。</summary>
    private void BeginDragPickOffset(float mouseX)
    {
        dragAdjustsDelayTime = !string.IsNullOrEmpty(config.PreKey);
        if (dragAdjustsDelayTime)
        {
            dragPickMouseXOffset = mouseX - control.Timeline.TimeToPixel(config.StartTime + config.DelayTime);
        }
        else
        {
            dragPickMouseXOffset = mouseX - control.Timeline.TimeToPixel(config.StartTime);
        }
    }

    private void ApplyClipDragFromMouseX(float mouseX)
    {
        var time = control.Timeline.PixelToTime(mouseX - dragPickMouseXOffset);
        time = (float)control.Timeline.SnapToFrameIfRequired(time);
        if (dragAdjustsDelayTime)
        {
            config.DelayTime = Mathf.Max(0f, time - config.StartTime);
        }
        else
        {
            config.StartTime = Mathf.Max(0f, time);
        }
        control.Timeline.Repaint();
    }

    #endregion

    #region Event

    private List<TimelineEventBase> events = new List<TimelineEventBase>();
    public List<TimelineEventBase> Events => events;

    protected virtual void DrawEvents()
    {
        for (var i = 0; i < events.Count; i++)
        {
            events[i].Draw();
        }
    }

    private void InitEvents()
    {
        events.Clear();
        if (this.config.EventConfigs == null)
        {
            return;
        }
        for (int i = 0; i < this.config.EventConfigs.Count; i++)
        {
            var eventConfig = this.config.EventConfigs[i];
            var e = CreateEvent(eventConfig);
            events.Add(e);
        }
    }

    public TimelineEventBase CreateEvent(TimelineEventConfigBase eventConfig)
    {
        var e = new TimelineEventBase();
        e.Init(this.control, this, eventConfig);
        return e;
    }

    public virtual List<TimelineEventBase> GetAllEvents()
    {
        return events;
    }

    public virtual bool SyncEventConfigsBeforeSave()
    {
        if (config == null)
        {
            return false;
        }

        config.EventConfigs = new List<TimelineEventConfigBase>();
        for (var i = 0; i < this.events.Count; i++)
        {
            config.EventConfigs.Add(this.events[i].Config);
        }
        return true;
    }

    public virtual void RemoveEvent(TimelineEventBase e)
    {
        if (this.events.Remove(e))
        {
            var nextClip = this.control.Clips.Find(o => o.Config.PreKey == e.Config.EventKey);
            if (nextClip != null)
            {
                nextClip.Config.PreKey = "";
            }
        }
    }

    #endregion

    #region Node 与连线

    private List<TimelineNodeBase> nodes = new List<TimelineNodeBase>();
    public List<TimelineNodeBase> Nodes => nodes;

    private List<TimelineLineBase> lines = new List<TimelineLineBase>();

    private void InitNodes()
    {
        var startNode = new TimelineNodeBase();
        startNode.Init(control, this, TimelineNodeKind.Start);
        nodes.Add(startNode);

        var endNode = new TimelineNodeBase();
        endNode.Init(control, this, TimelineNodeKind.End);
        nodes.Add(endNode);
    }

    private void DrawNodes()
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            nodes[i].Draw();
        }
    }

    public Vector2 GetPreLinePoint()
    {
        return new Vector2(rect.x, rect.y + rect.height * 0.5f);
    }

    public Vector2 GetEndLinePoint()
    {
        return new Vector2(rect.x + rect.width, rect.y + rect.height * 0.5f);
    }

    /// <summary>绘制指向后续 Clip 的前置连线。</summary>
    private void DrawNextClipLineLogic()
    {
        var nextClips = this.control.Clips.FindAll(o => o.Config.PreKey == this.config.ClipKey);

        if (nextClips.Count == 0)
        {
            return;
        }

        if (lines.Count < nextClips.Count)
        {
            var addCount = nextClips.Count - lines.Count;
            for (int i = 0; i < addCount; i++)
            {
                lines.Add(new TimelineLineBase());
            }
        }

        for (int i = 0; i < nextClips.Count; i++)
        {
            lines[i].Draw(GetEndLinePoint(), new Vector2(1, 0), nextClips[i].GetPreLinePoint(), new Vector2(-1, 0));
        }
    }

    #endregion

    #region PreKey 约束

    /// <summary>有 PreKey 时，StartTime 跟随前置 Clip 或 Event 自动对齐。</summary>
    protected virtual void AdjustStartTimeByPreClipKeyLogic()
    {
        if (string.IsNullOrEmpty(this.config.PreKey))
        {
            return;
        }

        var preClip = this.control.Clips.Find(o => this.config.PreKey.Equals(o.config.ClipKey));

        if (preClip != null)
        {
            this.config.StartTime = preClip.Config.StartTime + preClip.Config.DelayTime + preClip.Config.Duration;
            return;
        }

        var events = this.control.Clips.SelectMany(o => o.GetAllEvents()).ToList();

        var preEvent = events.Find(o => this.config.PreKey.Equals(o.Config.EventKey));
        if (preEvent != null)
        {
            this.config.StartTime = preEvent.Clip.GetContentTimelineStartTime()
                + TimelineSchedule.GetEventOffset(preEvent.Clip.Config, preEvent.Config);
        }
    }

    #endregion

    #region 生命周期

    /// <summary>
    /// 按逻辑名加载 Clip 用的 prefab。默认按特效解析；单位类 Clip（如 PlayAnimator）覆写。
    /// </summary>
    protected virtual GameObject LoadClipPrefab(string logicalName)
    {
        return EditorTimelineAssetPathUtil.LoadEffectPrefab(logicalName);
    }

    /// <summary>预览画质切换后重载 prefab（逻辑名不变）。</summary>
    public virtual void OnPreviewQualityChanged()
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            return;
        }

        prefab = LoadClipPrefab(prefabPath);
        if (instance != null && !isBaseTransform)
        {
            UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            ChangeClipTimeStateType(ClipTimeStateType.OutTime);
        }
    }

    /// <summary>
    /// 施法者预制体路径变更或强制重建后回调。
    /// isBaseTransform 的 Clip 必须重绑 instance，否则 Destroy 后仍握着旧对象，预览会静默失败。
    /// </summary>
    public virtual void OnSetMyTransformPrefabPath(string path)
    {
        if (!isBaseTransform || control == null || control.MyTransform == null)
        {
            return;
        }

        instance = control.MyTransform.gameObject;
        prefabPath = path;
        prefab = EditorTimelineAssetPathUtil.LoadUnitPrefab(path);
    }

    public virtual void OnDestroy()
    {
        if (instance != null && !isBaseTransform)
        {
            GameObject.DestroyImmediate(instance);
            instance = null;
        }
    }

    /// <summary>停止预览时重置为 OutTime 状态（OnOutTime 已在状态切换时执行）。</summary>
    public virtual void OnStopPreview()
    {
        ChangeClipTimeStateType(ClipTimeStateType.OutTime);
    }

    #endregion
}
