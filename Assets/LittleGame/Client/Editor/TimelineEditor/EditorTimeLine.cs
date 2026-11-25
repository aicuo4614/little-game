using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DMTimeArea;
using LittleGame.Client.SkillTimeline;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor.Timeline
{
    /// <summary>
    /// 像素级还原 Demo client-demo-assets 的 EditorTimeLine 完整操作体感：
    /// 1. 打开菜单自动切到独立预览场景 TimelineEditor.unity，关闭自动恢复原场景；
    /// 2. 顶部工具栏（新建/加载/保存/另存为/快捷键 + SettingIcon）；
    /// 3. 左上播放控制条（上一帧/播放Toggle/下一帧/停止 + 当前秒数）；
    /// 4. 左侧可拖宽属性栏（预览角色/阵营 + Clip 搜索定位 + Timeline/Clip/Event 属性）；
    /// 5. 右侧 SimpleTimeArea 斑马纹画布（YToPixel/PixelToY 原版坐标、滚轮平移、Ctrl/Shift 滚轮缩放、拖线边缘自动滚屏、视图状态持久化、非 Play 实时逐帧预览）。
    /// </summary>
    public sealed class EditorTimeLine : SimpleTimeArea
    {
        private const float MinLeftPanelWidth = 200f;
        private const float MaxLeftPanelWidth = 800f;
        private const float LeftResizeHandleWidth = 6f;
        private const float InitialShownTimeMin = 0f;
        private const float InitialShownTimeMax = 3f;
        private const float InitialShownYMin = 0f;
        private const float InitialShownYMax = 150f;
        private const int ClipRowStep = 10;

        private const float CanvasScrollWheelPixelsPerStep = 16f;
        private const float ConnectDragAutoScrollEdgePixels = 32f;
        private const float ConnectDragAutoScrollMinSpeed = 50f;
        private const float ConnectDragAutoScrollMaxSpeed = 300f;

        private static readonly Color RowStripeColorA = new Color(0x38 / 255f, 0x38 / 255f, 0x38 / 255f, 1f);
        private static readonly Color RowStripeColorB = new Color(0x2F / 255f, 0x2F / 255f, 0x2F / 255f, 1f);

        private const string LeftWidthPrefsKey = "LittleGame.EditorTimeLine.LeftPanelWidth";
        private const string ViewHasPrefsKey = "LittleGame.EditorTimeLine.ViewState.HasSaved";
        private const string ViewScaleXKey = "LittleGame.EditorTimeLine.ViewState.ScaleX";
        private const string ViewScaleYKey = "LittleGame.EditorTimeLine.ViewState.ScaleY";
        private const string ViewTransXKey = "LittleGame.EditorTimeLine.ViewState.TransX";
        private const string ViewTransYKey = "LittleGame.EditorTimeLine.ViewState.TransY";
        private const string LastTimelineIdPrefsKey = "LittleGame.EditorTimeLine.LastTimelineId";

        private float leftPanelWidth = 300f;
        private bool isResizingLeftPanel;
        private Vector2 leftPanelScrollPosition;

        private Rect rectTopBar;
        private Rect rectLeftTopToolBar;
        private Rect rectLeft;
        private Rect rectTotalArea;
        private Rect rectTimeRuler;
        private Rect rectContent;
        private Vector2 lastMousePosition;

        private readonly SkillTimelineLibrary library = new SkillTimelineLibrary();
        private TimelineControl timelineControl;
        private string[] timelineIds = Array.Empty<string>();
        private int selectedIndex;
        private string currentFilePath;
        private bool dirty;
        private bool isPlaying;
        private double runningTime;
        private double cutOffTime = 1.5;
        private double lastUpdateTime;
        private double connectAutoScrollLastTime;

        private int yConst = -1;
        private bool timeAreaViewInitialized;
        private Vector2 lastPersistedScale;
        private Vector2 lastPersistedTranslation;
        private bool hasLastPersistedTransform;

        public bool IsDirty => dirty;
        public override double RunningTime { get => runningTime; set => runningTime = Math.Max(0.0, value); }
        public override double CutOffTime { get => cutOffTime; set => cutOffTime = Math.Max(0.1, value); }
        protected override bool IsLockedMoveFrame => isPlaying || Application.isPlaying;
        protected override bool IsLockDragHeaderArrow => isPlaying;
        public override Rect _rectTimeAreaTotal => rectTotalArea;
        public override Rect _rectTimeAreaContent => rectContent;
        public override Rect _rectTimeAreaRuler => rectTimeRuler;
        protected override float sequencerHeaderWidth => leftPanelWidth;

        [MenuItem("Little Game/Combat/技能编辑器", false, 2002)]
        public static void DoWindow()
        {
            if (!Application.isPlaying && !TimelinePreviewPlayback.OpenPreviewSceneIfNeeded())
                return;

            var win = GetWindow<EditorTimeLine>(false, "技能编辑器");
            win.minSize = new Vector2(640f, 360f);
            win.Show();
            win.EnsureInitialized();
            win.RefreshTitle();
        }

        private void OnEnable()
        {
            leftPanelWidth = Mathf.Clamp(EditorPrefs.GetFloat(LeftWidthPrefsKey, 300f), MinLeftPanelWidth, MaxLeftPanelWidth);
            timeAreaViewInitialized = false;
            yConst = -1;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            lastUpdateTime = EditorApplication.timeSinceStartup;
            EnsureInitialized();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            PersistTimeAreaViewStateIfChanged();
        }

        private void OnDestroy()
        {
            if (dirty && timelineControl?.Config != null)
            {
                if (EditorUtility.DisplayDialog("未保存的技能修改", $"技能 {timelineControl.Config.TimelineId} 有未保存的改动，关闭前是否保存？", "保存", "放弃"))
                    SaveCurrentTimeline();
            }
            timelineControl?.OnDestroy();
            TimelinePreviewPlayback.RestorePreviousSceneIfNeeded();
        }

        private void EnsureInitialized()
        {
            if (timelineControl == null) timelineControl = new TimelineControl(this);
            if (timelineControl.Config == null)
                ReloadLibrary(EditorPrefs.GetString(LastTimelineIdPrefsKey, "archer_pierce"));
        }

        public void ReloadLibrary(string selectId = null)
        {
            library.Reload();
            timelineIds = library.Timelines.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            if (!string.IsNullOrEmpty(selectId))
            {
                int idx = Array.IndexOf(timelineIds, selectId);
                if (idx >= 0) selectedIndex = idx;
            }
            selectedIndex = Mathf.Clamp(selectedIndex, 0, Math.Max(0, timelineIds.Length - 1));
            LoadSelectedTimeline();
        }

        private void LoadSelectedTimeline()
        {
            StopPreView();
            dirty = false;
            if (timelineIds.Length == 0)
            {
                timelineControl.Init(null, library);
                currentFilePath = null;
                RefreshTitle();
                return;
            }

            string id = timelineIds[selectedIndex];
            EditorPrefs.SetString(LastTimelineIdPrefsKey, id);
            var raw = library.Get(id);
            var copy = raw != null ? SkillTimelineJson.Deserialize<SkillTimelineConfig>(SkillTimelineJson.Serialize(raw)) : null;
            currentFilePath = Path.Combine(SkillTimelineLibrary.SkillsAssetFolder, id + ".json").Replace('\\', '/');
            timelineControl.Init(copy, library);
            RefreshTitle();
            timelineControl.EvaluatePreview();
            Repaint();
        }

        public object ConfigurePreview(float distance, float angle, float speed, int count, float time)
        {
            timelineControl.PreviewPlayback.SetGeometry(distance, angle, speed, count);
            RunningTime = time; timelineControl.EvaluatePreview(); Repaint();
            var cfg = timelineControl.Config;
            return new { timelineId = cfg?.TimelineId, distance, angle, targetSpeed = speed, targetCount = count, time, ruleTiming = cfg == null ? null : new SkillTimelineSchedule(cfg).BuildDefinition(cfg), previewOnly = true };
        }

        public void MarkDirty()
        {
            dirty = true;
            RefreshTitle();
        }

        private void RefreshTitle()
        {
            string id = timelineControl?.Config?.TimelineId;
            string name = string.IsNullOrEmpty(id) ? "技能编辑器" : $"技能编辑器 - {id}";
            titleContent = new GUIContent(dirty ? name + " *" : name);
        }

        public void PlayPreview()
        {
            isPlaying = true;
            if (runningTime >= cutOffTime) runningTime = 0.0;
            lastUpdateTime = EditorApplication.timeSinceStartup;
        }

        public void PausePreView()
        {
            isPlaying = false;
        }

        public void StopPreView()
        {
            isPlaying = false;
            runningTime = 0.0;
            timelineControl?.PreviewPlayback?.Stop();
            Repaint();
        }

        private void PreviousTimeFrame()
        {
            isPlaying = false;
            float step = _frameRate > 0f ? 1f / _frameRate : 1f / 30f;
            runningTime = Math.Max(0.0, runningTime - step);
            timelineControl?.EvaluatePreview();
            Repaint();
        }

        private void NextTimeFrame()
        {
            isPlaying = false;
            float step = _frameRate > 0f ? 1f / _frameRate : 1f / 30f;
            runningTime = Math.Min(cutOffTime, runningTime + step);
            timelineControl?.EvaluatePreview();
            Repaint();
        }

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            double dt = Math.Max(0.0, now - lastUpdateTime);
            lastUpdateTime = now;
            if (!Application.isPlaying && isPlaying)
            {
                runningTime += dt;
                if (runningTime >= cutOffTime)
                {
                    runningTime = cutOffTime;
                    PausePreView();
                }
                timelineControl?.EvaluatePreview();
                Repaint();
            }
        }

        private void OnGUI()
        {
            EnsureInitialized();
            if (!Application.isPlaying && !TimelinePreviewPlayback.IsPreviewSceneActive())
            {
                EditorGUILayout.HelpBox("当前不在独立技能预览场景 TimelineEditor.unity，点击下方按钮切入预览场景即可实时查看 3D 动作与粒子效果。", MessageType.Info);
                if (GUILayout.Button("切入技能预览场景 (TimelineEditor.unity)", GUILayout.Height(32)))
                {
                    TimelinePreviewPlayback.OpenPreviewSceneIfNeeded();
                    timelineControl.PreviewPlayback.Invalidate();
                    timelineControl.EvaluatePreview();
                }
                return;
            }

            lastMousePosition = Event.current != null ? Event.current.mousePosition : Vector2.zero;

            Rect rectMainBodyArea = new Rect(0f, toolbarHeight, position.width, Mathf.Max(120f, position.height - toolbarHeight));
            rectTopBar = new Rect(0f, 0f, position.width, toolbarHeight);
            rectLeftTopToolBar = new Rect(rectMainBodyArea.x, rectMainBodyArea.y, leftPanelWidth, timeRulerHeight);
            rectLeft = new Rect(rectMainBodyArea.x, rectMainBodyArea.y + timeRulerHeight, leftPanelWidth, rectMainBodyArea.height - timeRulerHeight);
            rectTotalArea = new Rect(rectMainBodyArea.x + leftPanelWidth, rectMainBodyArea.y, Mathf.Max(180f, position.width - leftPanelWidth), rectMainBodyArea.height);
            rectTimeRuler = new Rect(rectMainBodyArea.x + leftPanelWidth, rectMainBodyArea.y, rectTotalArea.width, timeRulerHeight);
            rectContent = new Rect(rectMainBodyArea.x + leftPanelWidth, rectMainBodyArea.y + timeRulerHeight, rectTotalArea.width, rectMainBodyArea.height - timeRulerHeight);

            HandleLeftPanelResize(rectMainBodyArea);

            InitTimeArea(false, false, true, true);
            ApplyInitialTimeAreaViewIfNeeded();
            timelineControl.ShortcutControl.Handle();
            HandleConnectDragAutoScroll();
            HandleCanvasScrollWheelPan();
            DrawTimeAreaBackGround();

            // 右侧画布（严格按 Demo 原版 BeginClip(GetTimeArea.drawRect) + 斑马纹 + Clips）
            GUI.BeginClip(GetTimeArea.drawRect);
            DrawVerticalTickLine();
            timelineControl.DrawCanvasContent();
            GUI.EndClip();

            double beforeRuler = runningTime;
            OnTimeRulerCursorAndCutOffCursorInput();
            if (Math.Abs(beforeRuler - runningTime) > 0.00001)
                timelineControl.EvaluatePreview();
            DrawTimeRulerArea();

            DrawTopToolBar();
            DrawLeftContent();
            DrawLeftTopToolBar();

            PersistTimeAreaViewStateIfChanged();
        }

        #region 原版坐标系、斑马纹与视图持久化

        public float WorldPixelXToLocal(float pixelX) => pixelX - GetTimeArea.drawRect.x;
        public float WorldPixelYToLocal(float pixelY) => pixelY - GetTimeArea.drawRect.y;
        public float LocalPixelXToWorld(float localX) => localX + GetTimeArea.drawRect.x;
        public float LocalPixelYToWorld(float localY) => localY + GetTimeArea.drawRect.y;

        private int YConst
        {
            get
            {
                if (yConst == -1 && GetTimeArea != null)
                    yConst = GetPreviousMultiple(PixelToY(GetTimeArea.drawRect.y), ClipRowStep);
                return yConst;
            }
        }

        public float GetYByIndex(int index) => YConst - ClipRowStep * index;
        public int GetIndexByY(float y) => (int)((YConst - y) / ClipRowStep);
        public int GetIndexByMousePosition() => GetIndexByY(PixelToY(lastMousePosition.y));
        public float GetItemPixelHeight() => Mathf.Max(12f, Mathf.Abs(YToPixel(GetYByIndex(0)) - YToPixel(GetYByIndex(1))));

        public void LocateClip(TimelineClipBase clip)
        {
            int idx = timelineControl.Clips.IndexOf(clip);
            if (idx < 0) return;
            var area = GetTimeArea;
            Rect target = clip.ComputeRect(idx);
            float focusX = target.x + Mathf.Min(target.width * 0.5f, area.drawRect.width * 0.25f);
            Vector2 offset = new Vector2(area.drawRect.width * 0.5f - focusX, area.drawRect.height * 0.5f - target.center.y);
            area.SetTransform(area.translation + offset, area.scale);
            clip.FlashForLocate();
            Repaint();
        }

        protected override void DrawVerticalTickLine()
        {
            float drawWidth = GetTimeArea.drawRect.width;
            float drawHeight = GetTimeArea.drawRect.height;
            if (drawWidth <= 0f || drawHeight <= 0f) return;

            float maxY = PixelToY(GetTimeArea.drawRect.y);
            float minY = PixelToY(GetTimeArea.drawRect.yMax);
            var list = GetSequenceInRange(YConst, ClipRowStep, minY, maxY);
            if (list.Count < 2)
            {
                EditorGUI.DrawRect(new Rect(0f, 0f, drawWidth, drawHeight), RowStripeColorA);
                return;
            }

            var boundaries = new List<(float localY, float worldY)>(list.Count);
            for (int i = 0; i < list.Count; i++)
                boundaries.Add((WorldPixelYToLocal(YToPixel(list[i])), list[i]));
            boundaries.Sort((a, b) => a.localY.CompareTo(b.localY));

            if (boundaries[0].localY > 0f)
                DrawRowStripe(0f, boundaries[0].localY, PixelToY(LocalPixelYToWorld(0f)), boundaries[0].worldY, drawWidth);
            for (int i = 0; i < boundaries.Count - 1; i++)
                DrawRowStripe(boundaries[i].localY, boundaries[i + 1].localY, boundaries[i].worldY, boundaries[i + 1].worldY, drawWidth);
            var last = boundaries[boundaries.Count - 1];
            if (last.localY < drawHeight)
                DrawRowStripe(last.localY, drawHeight, last.worldY, PixelToY(LocalPixelYToWorld(drawHeight)), drawWidth);
        }

        private void DrawRowStripe(float y0, float y1, float wy0, float wy1, float w)
        {
            float top = Mathf.Max(0f, Mathf.Min(y0, y1));
            float bottom = Mathf.Min(GetTimeArea.drawRect.height, Mathf.Max(y0, y1));
            if (bottom <= top) return;
            int rowIndex = GetIndexByY((wy0 + wy1) * 0.5f);
            EditorGUI.DrawRect(new Rect(0f, top, w, bottom - top), (rowIndex & 1) == 0 ? RowStripeColorA : RowStripeColorB);
        }

        private static int GetPreviousMultiple(float value, int step) => Mathf.FloorToInt(value / step) * step;

        private static List<float> GetSequenceInRange(int anchor, int step, float minY, float maxY)
        {
            var result = new List<float>();
            float low = Mathf.Min(minY, maxY);
            float high = Mathf.Max(minY, maxY);
            int startK = Mathf.FloorToInt((low - anchor) / step);
            int endK = Mathf.CeilToInt((high - anchor) / step);
            for (int k = startK; k <= endK; k++) result.Add(anchor + k * step);
            return result;
        }

        private void ApplyInitialTimeAreaViewIfNeeded()
        {
            if (timeAreaViewInitialized || GetTimeArea == null || GetTimeArea.drawRect.width < 1f || GetTimeArea.drawRect.height < 1f) return;

            if (EditorPrefs.GetBool(ViewHasPrefsKey, false))
            {
                float sx = EditorPrefs.GetFloat(ViewScaleXKey, 0f);
                float sy = EditorPrefs.GetFloat(ViewScaleYKey, 0f);
                if (Mathf.Abs(sx) > 1E-05f && Mathf.Abs(sy) > 1E-05f)
                {
                    Vector2 tr = new Vector2(EditorPrefs.GetFloat(ViewTransXKey, 0f), EditorPrefs.GetFloat(ViewTransYKey, 0f));
                    GetTimeArea.SetTransform(tr, new Vector2(sx, sy));
                    yConst = -1;
                    timeAreaViewInitialized = true;
                    CacheTransform();
                    return;
                }
            }

            GetTimeArea.SetShownHRangeInsideMargins(InitialShownTimeMin, InitialShownTimeMax);
            GetTimeArea.SetShownVRangeInsideMargins(InitialShownYMin, InitialShownYMax);
            yConst = -1;
            timeAreaViewInitialized = true;
            CacheTransform();
        }

        private void PersistTimeAreaViewStateIfChanged()
        {
            if (!timeAreaViewInitialized || GetTimeArea == null) return;
            var s = GetTimeArea.scale;
            var t = GetTimeArea.translation;
            if (hasLastPersistedTransform && (s - lastPersistedScale).sqrMagnitude < 1e-8f && (t - lastPersistedTranslation).sqrMagnitude < 1e-8f)
                return;

            EditorPrefs.SetFloat(LeftWidthPrefsKey, leftPanelWidth);
            EditorPrefs.SetBool(ViewHasPrefsKey, true);
            EditorPrefs.SetFloat(ViewScaleXKey, s.x);
            EditorPrefs.SetFloat(ViewScaleYKey, s.y);
            EditorPrefs.SetFloat(ViewTransXKey, t.x);
            EditorPrefs.SetFloat(ViewTransYKey, t.y);
            CacheTransform();
        }

        private void CacheTransform()
        {
            lastPersistedScale = GetTimeArea.scale;
            lastPersistedTranslation = GetTimeArea.translation;
            hasLastPersistedTransform = true;
        }

        private void HandleCanvasScrollWheelPan()
        {
            var evt = Event.current;
            if (evt == null || evt.type != EventType.ScrollWheel) return;
            if (!rectContent.Contains(evt.mousePosition)) return;
            if (evt.control || evt.shift || evt.alt) return;
            var area = GetTimeArea;
            if (area == null || area.drawRect.height < 1f) return;
            var tr = area.translation;
            tr.y -= evt.delta.y * CanvasScrollWheelPixelsPerStep;
            area.SetTransform(tr, area.scale);
            evt.Use();
            GUI.changed = true;
        }

        private void HandleConnectDragAutoScroll()
        {
            bool connecting = timelineControl != null && (timelineControl.MenuControl.IsConnecting || timelineControl.IsNodeDragConnecting);
            if (!connecting) { connectAutoScrollLastTime = 0d; return; }
            if (Event.current.type != EventType.Repaint) return;

            var area = GetTimeArea;
            if (area == null || area.drawRect.height < 1f) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = connectAutoScrollLastTime <= 0d ? 0.016f : Mathf.Clamp((float)(now - connectAutoScrollLastTime), 0f, 0.05f);
            connectAutoScrollLastTime = now;

            float intensity = 0f;
            float edge = ConnectDragAutoScrollEdgePixels;
            if (lastMousePosition.y < rectContent.y + edge)
                intensity = Mathf.Clamp01((rectContent.y + edge - lastMousePosition.y) / edge);
            else if (lastMousePosition.y > rectContent.yMax - edge)
                intensity = -Mathf.Clamp01((lastMousePosition.y - (rectContent.yMax - edge)) / edge);

            if (Mathf.Abs(intensity) < 0.001f) return;
            float speed = Mathf.Lerp(ConnectDragAutoScrollMinSpeed, ConnectDragAutoScrollMaxSpeed, Mathf.Abs(intensity)) * Mathf.Sign(intensity);
            var tr = area.translation;
            tr.y += speed * dt;
            area.SetTransform(tr, area.scale);
            Repaint();
        }

        #endregion

        #region 顶部 / 左上 / 左侧原版面板

        private void HandleLeftPanelResize(Rect body)
        {
            float maxW = Mathf.Max(MinLeftPanelWidth, position.width - 120f);
            leftPanelWidth = Mathf.Clamp(leftPanelWidth, MinLeftPanelWidth, Mathf.Min(MaxLeftPanelWidth, maxW));
            Rect handle = new Rect(leftPanelWidth - LeftResizeHandleWidth * 0.5f, body.y, LeftResizeHandleWidth, position.height - body.y);
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeHorizontal);

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && handle.Contains(evt.mousePosition)) { isResizingLeftPanel = true; evt.Use(); }
            else if (evt.type == EventType.MouseDrag && isResizingLeftPanel) { leftPanelWidth = Mathf.Clamp(leftPanelWidth + evt.delta.x, MinLeftPanelWidth, Mathf.Min(MaxLeftPanelWidth, maxW)); Repaint(); evt.Use(); }
            else if (evt.type == EventType.MouseUp && isResizingLeftPanel && evt.button == 0) { isResizingLeftPanel = false; evt.Use(); }
        }

        private void DrawTopToolBar()
        {
            GUILayout.BeginArea(rectTopBar);
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(44))) OnNewTimeline();
            if (GUILayout.Button("加载", EditorStyles.toolbarButton, GUILayout.Width(44))) OnLoadTimeline();
            if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(44))) SaveCurrentTimeline();
            if (GUILayout.Button("另存为", EditorStyles.toolbarButton, GUILayout.Width(52))) SaveTimelineAs();
            if (GUILayout.Button(new GUIContent("快捷键", "查看技能编辑器快捷键说明"), EditorStyles.toolbarButton, GUILayout.Width(52)))
                TimelineEditorShortcutControl.ShowHelpDialog();

            GUILayout.FlexibleSpace();
            if (!Application.isPlaying && GUILayout.Button(ResManager.SettingIcon, EditorStyles.toolbarButton, GUILayout.Width(30)))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("帧率 30 FPS"), Mathf.Approximately(_frameRate, 30f), () => _frameRate = 30f);
                menu.AddItem(new GUIContent("帧率 60 FPS"), Mathf.Approximately(_frameRate, 60f), () => _frameRate = 60f);
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("重置画布缩放"), false, () =>
                {
                    GetTimeArea.SetShownHRangeInsideMargins(InitialShownTimeMin, InitialShownTimeMax);
                    GetTimeArea.SetShownVRangeInsideMargins(InitialShownYMin, InitialShownYMax);
                    yConst = -1;
                    Repaint();
                });
                menu.ShowAsContext();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawLeftTopToolBar()
        {
            GUILayout.BeginArea(rectLeftTopToolBar, string.Empty, EditorStyles.toolbarButton);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(ResManager.prevKeyContent, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                PreviousTimeFrame();

            bool nextPlaying = GUILayout.Toggle(isPlaying, ResManager.playContent, EditorStyles.toolbarButton);
            if (!Application.isPlaying && nextPlaying != isPlaying)
            {
                if (nextPlaying) PlayPreview();
                else PausePreView();
            }

            if (GUILayout.Button(ResManager.nextKeyContent, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                NextTimeFrame();

            if (GUILayout.Button(ResManager.StopIcon, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)) && !Application.isPlaying)
                StopPreView();

            GUILayout.FlexibleSpace();
            GUILayout.Label(TimeAsString(runningTime, "F2"));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawLeftContent()
        {
            GUILayout.BeginArea(rectLeft);
            leftPanelScrollPosition = EditorGUILayout.BeginScrollView(leftPanelScrollPosition);
            timelineControl.DrawLeftPanelContent(library, timelineIds, selectedIndex, idx =>
            {
                selectedIndex = idx;
                LoadSelectedTimeline();
            });
            EditorGUILayout.Space(24f);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        #endregion

        #region 新建 / 加载 / 保存 / 另存为

        private void OnNewTimeline()
        {
            StopPreView();
            var cfg = new SkillTimelineConfig
            {
                SchemaVersion = 1,
                TimelineId = "new_skill_" + DateTime.Now.ToString("HHmmss"),
                DisplayName = "新技能",
                Clips = new List<SkillClipConfig>
                {
                    new SkillClipConfig { ClipKey = "c_anim", Type = SkillClipType.PlayAnimation, ActionName = "Attack", Duration = 0.35f, Speed = 1f }
                }
            };
            currentFilePath = null;
            timelineControl.Init(cfg, library);
            MarkDirty();
        }

        private void OnLoadTimeline()
        {
            string dir = Path.GetFullPath(SkillTimelineLibrary.SkillsAssetFolder);
            string path = EditorUtility.OpenFilePanel("加载技能时间轴 JSON", dir, "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var cfg = SkillTimelineJson.Deserialize<SkillTimelineConfig>(File.ReadAllText(path));
                if (cfg == null) throw new Exception("JSON 为空");
                StopPreView();
                currentFilePath = path;
                dirty = false;
                timelineControl.Init(cfg, library);
                RefreshTitle();
                timelineControl.EvaluatePreview();
                Repaint();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("加载失败", ex.Message, "知道了");
            }
        }

        public void SaveCurrentTimeline()
        {
            if (timelineControl?.Config == null) return;
            timelineControl.RebuildSchedule();
            if (!string.IsNullOrEmpty(timelineControl.ScheduleError))
            {
                EditorUtility.DisplayDialog("无法保存", timelineControl.ScheduleError, "知道了");
                return;
            }
            Directory.CreateDirectory(SkillTimelineLibrary.SkillsAssetFolder);
            string targetPath = string.IsNullOrEmpty(currentFilePath)
                ? Path.Combine(SkillTimelineLibrary.SkillsAssetFolder, timelineControl.Config.TimelineId + ".json")
                : currentFilePath;
            File.WriteAllText(targetPath, SkillTimelineJson.Serialize(timelineControl.Config) + "\n");
            currentFilePath = targetPath.Replace('\\', '/');
            if (currentFilePath.StartsWith("Assets/")) AssetDatabase.ImportAsset(currentFilePath);
            else AssetDatabase.Refresh();

            dirty = false;
            RefreshTitle();
            if (Application.isPlaying && ClientBootstrap.Current?.View?.TimelineDirector != null)
                ClientBootstrap.Current.View.TimelineDirector.ReloadLibrary();
            library.Reload();
            timelineIds = library.Timelines.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            int idx = Array.IndexOf(timelineIds, timelineControl.Config.TimelineId);
            if (idx >= 0) selectedIndex = idx;
            ShowNotification(new GUIContent("已保存 " + timelineControl.Config.TimelineId));
        }

        private void SaveTimelineAs()
        {
            if (timelineControl?.Config == null) return;
            string dir = Path.GetFullPath(SkillTimelineLibrary.SkillsAssetFolder);
            string path = EditorUtility.SaveFilePanel("另存技能 JSON", dir, timelineControl.Config.TimelineId + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;
            timelineControl.Config.TimelineId = Path.GetFileNameWithoutExtension(path);
            currentFilePath = path;
            SaveCurrentTimeline();
        }

        #endregion
    }
}
