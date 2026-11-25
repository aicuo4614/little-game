using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Client.SkillTimeline;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor.Timeline
{
    /// <summary>
    /// 沿用 Demo TimelineControl 完整交互与分层：
    /// 负责 Clip/Event/Node 管理、左键按住头尾蓝点拖拉连线、Catch 绑定、Clip 搜索定位闪烁、
    /// 左侧预览设置/搜索/属性绘制，以及驱动 TimelinePreviewPlayback 逐帧预览。
    /// </summary>
    public sealed class TimelineControl
    {
        public EditorTimeLine Timeline { get; }
        public SkillTimelineConfig Config { get; private set; }
        public SkillTimelineSchedule Schedule { get; private set; }
        public string ScheduleError { get; private set; }

        public List<TimelineClipBase> Clips { get; } = new List<TimelineClipBase>();
        public TimelineMenuControl MenuControl { get; }
        public TimelineEditorShortcutControl ShortcutControl { get; }
        public TimelinePreviewPlayback PreviewPlayback { get; }

        public TimelineClipBase SelectedClip { get; private set; }
        public TimelineEventBase SelectedEvent { get; private set; }
        public TimelineNodeBase SelectedNode { get; private set; }

        // 头尾蓝点直接左键拖拽连线
        private TimelineNodeBase dragConnectNode;
        private readonly TimelineLineBase dragConnectLine = new TimelineLineBase();
        public bool IsNodeDragConnecting => dragConnectNode != null;

        // EndByOtherClipEnd Catch 模式
        private TimelineClipBase pendingCatchOwner;
        private string catchFeedback;
        public bool HasPendingCatch => pendingCatchOwner != null;

        // Clip 搜索定位
        private string clipSearchKeyword = string.Empty;
        private readonly List<TimelineClipBase> clipSearchMatches = new List<TimelineClipBase>();
        private int clipSearchIndex = -1;

        // 片段/事件拖动状态
        private TimelineClipBase draggingClip;
        private TimelineEventBase draggingEvent;
        private float dragPickTimeOffset;

        public TimelineControl(EditorTimeLine timeline)
        {
            Timeline = timeline;
            MenuControl = new TimelineMenuControl(this);
            ShortcutControl = new TimelineEditorShortcutControl(this);
            PreviewPlayback = new TimelinePreviewPlayback();
        }

        public void Init(SkillTimelineConfig config, SkillTimelineLibrary library)
        {
            Config = config;
            Clips.Clear();
            SelectedClip = null;
            SelectedEvent = null;
            SelectedNode = null;
            CancelAllPendingModes();
            clipSearchMatches.Clear();
            clipSearchIndex = -1;

            if (Config?.Clips != null)
            {
                for (int i = 0; i < Config.Clips.Count; i++)
                {
                    var clip = new TimelineClipBase();
                    clip.Init(this, Config.Clips[i]);
                    Clips.Add(clip);
                }
            }

            RebuildSchedule();
            if (Config != null)
                PreviewPlayback.AutoSelectRolesForTimeline(Config.TimelineId, library);
            PreviewPlayback.Invalidate();
        }

        public void OnDestroy()
        {
            PreviewPlayback.Dispose();
        }

        public void CancelAllPendingModes()
        {
            MenuControl.CancelConnect();
            dragConnectNode = null;
            pendingCatchOwner = null;
            catchFeedback = null;
        }

        public bool IsWaitingEndByOtherClipCatch(TimelineClipBase clip) => pendingCatchOwner == clip;

        public void BeginEndByOtherClipCatch(TimelineClipBase owner)
        {
            pendingCatchOwner = owner;
            catchFeedback = null;
            Timeline.Repaint();
        }

        public void RebuildSchedule()
        {
            Schedule = null;
            ScheduleError = null;
            if (Config == null) return;
            try
            {
                Schedule = new SkillTimelineSchedule(Config);
                Timeline.CutOffTime = Math.Max(0.2, Schedule.TotalDuration);
            }
            catch (Exception ex)
            {
                ScheduleError = ex.Message;
            }
            PreviewPlayback.Invalidate();
        }

        public void MarkDirtyAndRebuild()
        {
            RebuildSchedule();
            Timeline.MarkDirty();
            Timeline.Repaint();
        }

        public float ResolveClipStart(SkillClipConfig clip) =>
            Schedule != null ? Schedule.GetStart(clip) : clip.StartTime + clip.DelayTime;

        public float ResolveClipEnd(SkillClipConfig clip) =>
            Schedule != null ? Schedule.GetEnd(clip) : clip.StartTime + clip.DelayTime + clip.Duration;

        public float TimeToLocalX(float time) => Timeline.WorldPixelXToLocal(Timeline.TimeToPixel(time));
        public float LocalXToTime(float localX) => Timeline.PixelToTime(Timeline.LocalPixelXToWorld(localX));

        public void EvaluatePreview()
        {
            if (Config != null && Timeline.RunningTime >= 0.0)
                PreviewPlayback.Evaluate(Config, (float)Timeline.RunningTime);
        }

        public void DrawCanvasContent()
        {
            EvaluatePreview();

            Event e = Event.current;
            Vector2 mousePos = e != null ? e.mousePosition : Vector2.zero;

            for (int i = 0; i < Clips.Count; i++)
                Clips[i].Draw(i, mousePos);

            MenuControl.DrawConnectingLine(mousePos);
            DrawNodeDragConnectLine(mousePos);
            HandleCanvasPointer(e, mousePos);
        }

        private void DrawNodeDragConnectLine(Vector2 mousePos)
        {
            if (dragConnectNode == null) return;
            Vector2 dir = dragConnectNode.Kind == TimelineNodeKind.Start ? new Vector2(-1f, 0f) : new Vector2(1f, 0f);
            dragConnectLine.Draw(dragConnectNode.GetCenter(), dir, mousePos, -dir);
        }

        public void DrawLeftPanelContent(SkillTimelineLibrary library, string[] allTimelineIds, int selectedTimelineIdx, Action<int> onPickTimeline)
        {
            // 1. 预览角色与阵营栏（对标 Demo DrawPreviewQualityBar + DrawPreviewCasterTeamBar）
            DrawPreviewUnitBar(allTimelineIds, selectedTimelineIdx, onPickTimeline);
            EditorGUILayout.Space(2f);

            // 2. Clip 显示名搜索栏（对标 Demo DrawClipSearchBar）
            DrawClipSearchBar();
            EditorGUILayout.Space(4f);

            // 3. 原版 Timeline + 选中 Clip/Event 属性区
            DrawInspectorFields(library);
        }

        private void DrawPreviewUnitBar(string[] allTimelineIds, int selectedTimelineIdx, Action<int> onPickTimeline)
        {
            if (allTimelineIds != null && allTimelineIds.Length > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("快捷切换", GUILayout.Width(64));
                int nextIdx = EditorGUILayout.Popup(selectedTimelineIdx, allTimelineIds);
                EditorGUILayout.EndHorizontal();
                if (nextIdx != selectedTimelineIdx) onPickTimeline?.Invoke(nextIdx);
            }

            EditorGUI.BeginChangeCheck();
            float distance = EditorGUILayout.Slider("目标距离", PreviewPlayback.Distance, .6f, 7f);
            float angle = EditorGUILayout.Slider("交战角度", PreviewPlayback.Angle, -180f, 180f);
            float move = EditorGUILayout.Slider("目标横移速度", PreviewPlayback.TargetSpeed, -2f, 2f);
            int count = EditorGUILayout.IntSlider("预览目标数量", PreviewPlayback.TargetCount, 1, 5);
            if (EditorGUI.EndChangeCheck()) { PreviewPlayback.SetGeometry(distance, angle, move, count); EvaluatePreview(); }
            EditorGUILayout.HelpBox("范围线为规则参考；移动/多目标用于演出预览，命中与中断需通过正常战斗验收。", MessageType.None);
            int casterIdx = Math.Max(0, Array.IndexOf(TimelinePreviewPlayback.PreviewRoles, PreviewPlayback.CasterRole));
            int targetIdx = Math.Max(0, Array.IndexOf(TimelinePreviewPlayback.PreviewRoles, PreviewPlayback.TargetRole));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("施法单位", GUILayout.Width(64));
            int nextCaster = EditorGUILayout.Popup(casterIdx, TimelinePreviewPlayback.PreviewRoles);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("目标靶子", GUILayout.Width(64));
            int nextTarget = EditorGUILayout.Popup(targetIdx, TimelinePreviewPlayback.PreviewRoles);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("预览阵营", GUILayout.Width(64));
            int teamIdx = GUILayout.Toolbar(PreviewPlayback.CasterIsEnemy ? 1 : 0, new[] { "我方(左)", "敌方(右)" });
            EditorGUILayout.EndHorizontal();

            if (nextCaster != casterIdx || nextTarget != targetIdx || (teamIdx == 1) != PreviewPlayback.CasterIsEnemy)
            {
                PreviewPlayback.SetPreviewRoles(
                    TimelinePreviewPlayback.PreviewRoles[nextCaster],
                    TimelinePreviewPlayback.PreviewRoles[nextTarget],
                    teamIdx == 1);
                EvaluatePreview();
            }
        }

        private void DrawClipSearchBar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string next = EditorGUILayout.TextField(clipSearchKeyword, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
                ApplyClipSearch(next);

            EditorGUI.BeginDisabledGroup(clipSearchMatches.Count == 0);
            if (GUILayout.Button("下一个", GUILayout.Width(52)))
                SelectNextSearchMatch();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(clipSearchKeyword))
                EditorGUILayout.LabelField($"匹配 {clipSearchMatches.Count} 条", EditorStyles.miniLabel);
        }

        private void ApplyClipSearch(string keyword)
        {
            clipSearchKeyword = keyword ?? string.Empty;
            clipSearchMatches.Clear();
            clipSearchIndex = -1;
            if (string.IsNullOrWhiteSpace(clipSearchKeyword)) return;

            for (int i = 0; i < Clips.Count; i++)
            {
                var c = Clips[i];
                if ((c.GetLabelText() ?? "").IndexOf(clipSearchKeyword, StringComparison.OrdinalIgnoreCase) >= 0
                    || (c.Config?.ClipKey ?? "").IndexOf(clipSearchKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    clipSearchMatches.Add(c);
            }
            if (clipSearchMatches.Count > 0)
            {
                clipSearchIndex = 0;
                LocateAndSelect(clipSearchMatches[0]);
            }
        }

        private void SelectNextSearchMatch()
        {
            if (clipSearchMatches.Count == 0) return;
            clipSearchIndex = (clipSearchIndex + 1) % clipSearchMatches.Count;
            LocateAndSelect(clipSearchMatches[clipSearchIndex]);
        }

        private void LocateAndSelect(TimelineClipBase clip)
        {
            SelectedClip = clip;
            SelectedEvent = null;
            SelectedNode = null;
            Timeline.LocateClip(clip);
        }

        private static string DrawRuleKey(string label, string selected, string[] keys)
        {
            int index = Array.IndexOf(keys, selected ?? "");
            if (index < 0) { EditorGUILayout.HelpBox("节点不存在: " + selected, MessageType.Error); index = 0; }
            return keys[EditorGUILayout.Popup(label, index, keys)];
        }

        private void DrawInspectorFields(SkillTimelineLibrary library)
        {
            if (!string.IsNullOrEmpty(catchFeedback))
                EditorGUILayout.HelpBox(catchFeedback, MessageType.Info);
            if (!string.IsNullOrEmpty(ScheduleError))
                EditorGUILayout.HelpBox("校验失败: " + ScheduleError, MessageType.Error);

            if (Config != null)
            {
                EditorGUILayout.LabelField("Timeline", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Timeline.IsDirty ? "*" : " ", GUILayout.Width(12));
                Config.TimelineId = EditorGUILayout.TextField("TimelineId", Config.TimelineId ?? string.Empty);
                EditorGUILayout.EndHorizontal();

                Config.DisplayName = EditorGUILayout.TextField("名称/备注", Config.DisplayName ?? string.Empty);
                if (Config.Execution == null) Config.Execution = new SkillExecutionConfig();
                var execution = Config.Execution;
                execution.Enabled = EditorGUILayout.Toggle("规则时序", execution.Enabled);
                if (execution.Enabled)
                {
                    var keys = new[] { "" }.Concat(Config.Clips.Select(c => c.ClipKey)).Concat(Config.Clips.SelectMany(c => c.EventConfigs ?? new List<SkillEventConfig>()).Select(e => e.EventKey)).ToArray();
                    execution.LaunchKey = DrawRuleKey("发射节点", execution.LaunchKey, keys);
                    execution.ImpactKey = DrawRuleKey("命中节点", execution.ImpactKey, keys);
                    execution.RecoveryKey = DrawRuleKey("动作结束节点", execution.RecoveryKey, keys);
                    execution.InterruptOnControl = EditorGUILayout.Toggle("受控打断", execution.InterruptOnControl);
                    execution.TrackTarget = EditorGUILayout.Toggle("弹道跟踪目标", execution.TrackTarget);
                    if (Schedule != null && string.IsNullOrEmpty(ScheduleError))
                    {
                        try { var rule = Schedule.BuildDefinition(Config); EditorGUILayout.LabelField($"发射 {rule.LaunchAt:F3}s / 命中 {rule.ImpactAt:F3}s / 结束 {rule.RecoveryEnd:F3}s", EditorStyles.wordWrappedMiniLabel); }
                        catch (Exception ex) { EditorGUILayout.HelpBox(ex.Message, MessageType.Error); }
                    }
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField("DurationBindClipKey", Config.DurationBindClipKey ?? string.Empty);
                EditorGUI.EndDisabledGroup();
                EditorGUI.BeginDisabledGroup(SelectedClip == null);
                if (GUILayout.Button("抓取End", GUILayout.Width(60)))
                {
                    Config.DurationBindClipKey = SelectedClip.Config.ClipKey;
                    MarkDirtyAndRebuild();
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.FloatField("Duration", Schedule != null ? Schedule.TotalDuration : 0f);
                EditorGUI.EndDisabledGroup();

                if (EditorGUI.EndChangeCheck())
                    MarkDirtyAndRebuild();
                EditorGUILayout.Space(6);
            }

            if (SelectedEvent != null) SelectedEvent.DrawInspector();
            if (SelectedClip != null) SelectedClip.DrawInspector();

            if (Config != null && library?.Bindings?.Bindings != null)
            {
                var refs = library.Bindings.Bindings.Where(b => b.TimelineId == Config.TimelineId).ToArray();
                if (refs.Length > 0)
                {
                    EditorGUILayout.Space(6);
                    EditorGUILayout.LabelField("单位 JSON 挂钩", EditorStyles.boldLabel);
                    foreach (var r in refs)
                        EditorGUILayout.LabelField($"• {r.UnitKind}/{r.Trigger} ({(string.IsNullOrEmpty(r.Branch) ? "默认" : r.Branch)} ≥{r.MinStar}★)", EditorStyles.miniLabel);
                }
            }
        }

        public void InsertNewClip(SkillClipType type, int rowIndex, float startTime)
        {
            var cfg = new SkillClipConfig
            {
                ClipKey = GenerateUniqueClipKey("c_" + type.ToString().ToLowerInvariant()),
                Type = type,
                StartTime = startTime,
                Duration = type == SkillClipType.PlayHit ? 0.14f : 0.30f
            };
            InsertClip(cfg, rowIndex);
        }

        public void InsertClip(SkillClipConfig cfg, int rowIndex)
        {
            if (Config == null) return;
            int idx = Mathf.Clamp(rowIndex, 0, Config.Clips.Count);
            Config.Clips.Insert(idx, cfg);
            var clip = new TimelineClipBase();
            clip.Init(this, cfg);
            Clips.Insert(idx, clip);
            SelectedClip = clip;
            SelectedEvent = null;
            SelectedNode = null;
            MarkDirtyAndRebuild();
        }

        public void RemoveClip(TimelineClipBase clip)
        {
            if (clip == null || Config == null) return;
            DisconnectSuccessors(clip.Config.ClipKey);
            if (clip.Config.EventConfigs != null)
                foreach (var ev in clip.Config.EventConfigs) DisconnectSuccessors(ev.EventKey);
            Clips.Remove(clip);
            Config.Clips.Remove(clip.Config);
            if (SelectedClip == clip) SelectedClip = null;
            if (SelectedEvent?.Clip == clip) SelectedEvent = null;
            if (SelectedNode?.Clip == clip) SelectedNode = null;
            MarkDirtyAndRebuild();
        }

        public void AddEventToClip(TimelineClipBase clip)
        {
            if (clip == null) return;
            clip.Config.EventConfigs ??= new List<SkillEventConfig>();
            var ev = new SkillEventConfig
            {
                EventKey = GenerateUniqueEventKey("ev_hit"),
                Time = Mathf.Min(0.10f, Mathf.Max(0f, clip.Config.Duration * 0.4f)),
                EventName = "hit"
            };
            clip.Config.EventConfigs.Add(ev);
            clip.RebuildEvents();
            SelectedClip = clip;
            SelectedEvent = clip.Events[clip.Events.Count - 1];
            MarkDirtyAndRebuild();
        }

        public void RemoveEvent(TimelineEventBase ev)
        {
            if (ev == null) return;
            DisconnectSuccessors(ev.Config.EventKey);
            ev.Clip.Config.EventConfigs?.Remove(ev.Config);
            ev.Clip.RebuildEvents();
            if (SelectedEvent == ev) SelectedEvent = null;
            MarkDirtyAndRebuild();
        }

        public void TrySetPreKey(TimelineClipBase clip, string preKey)
        {
            if (clip == null) return;
            string oldPre = clip.Config.PreKey;
            float oldStart = clip.Config.StartTime;
            float oldDelay = clip.Config.DelayTime;

            clip.Config.PreKey = preKey ?? "";
            if (!string.IsNullOrEmpty(clip.Config.PreKey)) clip.Config.StartTime = 0f;

            try
            {
                _ = new SkillTimelineSchedule(Config);
                MarkDirtyAndRebuild();
            }
            catch (Exception ex)
            {
                clip.Config.PreKey = oldPre;
                clip.Config.StartTime = oldStart;
                clip.Config.DelayTime = oldDelay;
                EditorUtility.DisplayDialog("无法建立连接", ex.Message, "知道了");
            }
        }

        public void DisconnectSuccessors(string sourceKey)
        {
            if (string.IsNullOrEmpty(sourceKey)) return;
            for (int i = 0; i < Clips.Count; i++)
            {
                if (Clips[i].Config.PreKey == sourceKey)
                {
                    float resolvedStart = ResolveClipStart(Clips[i].Config);
                    Clips[i].Config.PreKey = "";
                    Clips[i].Config.StartTime = Mathf.Max(0f, (float)Timeline.SnapToFrameIfRequired(resolvedStart));
                    Clips[i].Config.DelayTime = 0f;
                }
            }
            MarkDirtyAndRebuild();
        }

        public string GenerateUniqueClipKey(string baseKey)
        {
            string prefix = string.IsNullOrWhiteSpace(baseKey) ? "clip" : baseKey;
            if (!Clips.Any(c => c.Config.ClipKey == prefix)) return prefix;
            int n = 1;
            while (Clips.Any(c => c.Config.ClipKey == $"{prefix}_{n}")) n++;
            return $"{prefix}_{n}";
        }

        public string GenerateUniqueEventKey(string baseKey)
        {
            string prefix = string.IsNullOrWhiteSpace(baseKey) ? "ev" : baseKey;
            bool Exists(string k) => Clips.Any(c => c.Config.EventConfigs != null && c.Config.EventConfigs.Any(ev => ev.EventKey == k));
            if (!Exists(prefix)) return prefix;
            int n = 1;
            while (Exists($"{prefix}_{n}")) n++;
            return $"{prefix}_{n}";
        }

        private void HandleCanvasPointer(Event e, Vector2 mousePos)
        {
            if (e == null) return;

            if (e.type == EventType.MouseDown)
            {
                // 1. 优先命中头尾蓝圆点（支持直接左键按住拖出连线）
                var hitNode = HitTestNode(mousePos);
                if (hitNode != null)
                {
                    if (e.button == 0)
                    {
                        if (!MenuControl.TryCompleteConnectionToNode(hitNode))
                        {
                            SelectedNode = hitNode;
                            SelectedClip = hitNode.Clip;
                            SelectedEvent = null;
                            dragConnectNode = hitNode;
                        }
                        e.Use();
                        Timeline.Repaint();
                        return;
                    }
                    if (e.button == 1)
                    {
                        SelectedNode = hitNode;
                        SelectedClip = hitNode.Clip;
                        SelectedEvent = null;
                        MenuControl.ShowNodeMenu(hitNode);
                        e.Use();
                        return;
                    }
                }

                // 2. 命中 Event
                var hitEv = HitTestEvent(mousePos);
                if (hitEv != null)
                {
                    SelectedEvent = hitEv;
                    SelectedClip = hitEv.Clip;
                    SelectedNode = null;
                    if (e.button == 0)
                    {
                        draggingEvent = hitEv;
                        dragPickTimeOffset = LocalXToTime(mousePos.x) - hitEv.GetEventTimelineTime();
                        e.Use();
                        Timeline.Repaint();
                        return;
                    }
                    if (e.button == 1)
                    {
                        MenuControl.ShowEventMenu(hitEv);
                        e.Use();
                        return;
                    }
                }

                // 3. 命中 Clip
                var hitClip = HitTestClip(mousePos);
                if (hitClip != null)
                {
                    if (e.button == 0)
                    {
                        if (pendingCatchOwner != null && hitClip != pendingCatchOwner)
                        {
                            pendingCatchOwner.Config.EndByClipKey = hitClip.Config.ClipKey;
                            catchFeedback = $"Catch 成功：{pendingCatchOwner.GetLabelText()} 结束绑定到 {hitClip.GetLabelText()}";
                            pendingCatchOwner = null;
                            MarkDirtyAndRebuild();
                            e.Use();
                            return;
                        }
                        if (!MenuControl.TryCompleteConnectionToClip(hitClip))
                        {
                            SelectedClip = hitClip;
                            SelectedEvent = null;
                            SelectedNode = null;
                            draggingClip = hitClip;
                            dragPickTimeOffset = LocalXToTime(mousePos.x) - ResolveClipStart(hitClip.Config);
                        }
                        e.Use();
                        Timeline.Repaint();
                        return;
                    }
                    if (e.button == 1)
                    {
                        SelectedClip = hitClip;
                        SelectedEvent = null;
                        SelectedNode = null;
                        MenuControl.ShowClipMenu(hitClip);
                        e.Use();
                        return;
                    }
                }

                // 4. 命中空白处
                if (e.button == 0)
                {
                    CancelAllPendingModes();
                    SelectedClip = null;
                    SelectedEvent = null;
                    SelectedNode = null;
                    Timeline.Repaint();
                }
                else if (e.button == 1)
                {
                    int row = Mathf.Max(0, Timeline.GetIndexByMousePosition());
                    float time = Mathf.Max(0f, LocalXToTime(mousePos.x));
                    MenuControl.ShowMainMenu(row, time);
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (dragConnectNode != null)
                {
                    e.Use();
                    Timeline.Repaint();
                }
                else if (draggingEvent != null)
                {
                    float targetTime = (float)Timeline.SnapToFrameIfRequired(LocalXToTime(mousePos.x) - dragPickTimeOffset);
                    float localInClip = targetTime - ResolveClipStart(draggingEvent.Clip.Config);
                    var clip = draggingEvent.Clip.Config;
                    float scale = clip.Type == SkillClipType.PlayAnimation ? Mathf.Max(0.1f, clip.Speed) : 1f;
                    draggingEvent.Config.Time = Mathf.Clamp(localInClip * scale, 0f, Mathf.Max(0f, clip.Duration * scale));
                    MarkDirtyAndRebuild();
                    e.Use();
                }
                else if (draggingClip != null)
                {
                    float targetStart = (float)Timeline.SnapToFrameIfRequired(LocalXToTime(mousePos.x) - dragPickTimeOffset);
                    if (string.IsNullOrEmpty(draggingClip.Config.PreKey))
                    {
                        draggingClip.Config.StartTime = Mathf.Max(0f, targetStart);
                    }
                    else
                    {
                        float preEnd = ResolveClipStart(draggingClip.Config) - draggingClip.Config.DelayTime;
                        draggingClip.Config.DelayTime = Mathf.Max(0f, (float)Timeline.SnapToFrameIfRequired(targetStart - preEnd));
                    }
                    MarkDirtyAndRebuild();
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                if (dragConnectNode != null)
                {
                    CompleteNodeDragConnect(mousePos);
                    dragConnectNode = null;
                    e.Use();
                    Timeline.Repaint();
                }
                draggingClip = null;
                draggingEvent = null;
            }
            else if (e.type == EventType.MouseMove && MenuControl.IsConnecting)
            {
                Timeline.Repaint();
            }
        }

        private void CompleteNodeDragConnect(Vector2 mousePos)
        {
            var dstNode = HitTestNode(mousePos);
            if (dstNode != null && dstNode.Clip != dragConnectNode.Clip)
            {
                if (dragConnectNode.Kind == TimelineNodeKind.End && dstNode.Kind == TimelineNodeKind.Start)
                {
                    TrySetPreKey(dstNode.Clip, dragConnectNode.Clip.Config.ClipKey);
                    return;
                }
                if (dragConnectNode.Kind == TimelineNodeKind.Start && dstNode.Kind == TimelineNodeKind.End)
                {
                    TrySetPreKey(dragConnectNode.Clip, dstNode.Clip.Config.ClipKey);
                    return;
                }
            }

            var dstClip = HitTestClip(mousePos);
            if (dstClip != null && dstClip != dragConnectNode.Clip)
            {
                if (dragConnectNode.Kind == TimelineNodeKind.End)
                    TrySetPreKey(dstClip, dragConnectNode.Clip.Config.ClipKey);
                else
                    TrySetPreKey(dragConnectNode.Clip, dstClip.Config.ClipKey);
            }
        }

        private TimelineNodeBase HitTestNode(Vector2 pos)
        {
            for (int i = Clips.Count - 1; i >= 0; i--)
            {
                if (Clips[i].StartNode.Contains(pos)) return Clips[i].StartNode;
                if (Clips[i].EndNode.Contains(pos)) return Clips[i].EndNode;
            }
            return null;
        }

        private TimelineEventBase HitTestEvent(Vector2 pos)
        {
            for (int i = Clips.Count - 1; i >= 0; i--)
            {
                var evs = Clips[i].Events;
                for (int j = evs.Count - 1; j >= 0; j--)
                    if (evs[j].Rect.Contains(pos)) return evs[j];
            }
            return null;
        }

        private TimelineClipBase HitTestClip(Vector2 pos)
        {
            for (int i = Clips.Count - 1; i >= 0; i--)
                if (Clips[i].Rect.Contains(pos)) return Clips[i];
            return null;
        }
    }
}
