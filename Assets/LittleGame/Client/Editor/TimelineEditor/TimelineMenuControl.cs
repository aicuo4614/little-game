using System;
using LittleGame.Client.SkillTimeline;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor.Timeline
{
    /// <summary>沿用 Demo TimelineMenuControl：四套右键菜单、跨技能复制粘贴与连线预览。</summary>
    public sealed class TimelineMenuControl
    {
        private readonly TimelineControl control;
        private readonly TimelineLineBase previewLine = new TimelineLineBase();

        private TimelineClipBase connectClip;
        private TimelineEventBase connectEvent;
        private bool reverseConnect;

        private static SkillClipConfig copiedClip;
        public static bool HasCopiedClip => copiedClip != null;
        public bool IsConnecting => connectClip != null || connectEvent != null;

        public TimelineMenuControl(TimelineControl ctrl)
        {
            control = ctrl;
        }

        public void CancelConnect()
        {
            connectClip = null;
            connectEvent = null;
            reverseConnect = false;
        }

        public void DrawConnectingLine(Vector2 mousePos)
        {
            if (connectClip != null)
            {
                if (reverseConnect)
                    previewLine.Draw(connectClip.GetPreLinePoint(), new Vector2(-1f, 0f), mousePos, new Vector2(1f, 0f));
                else
                    previewLine.Draw(connectClip.GetEndLinePoint(), new Vector2(1f, 0f), mousePos, new Vector2(-1f, 0f));
            }
            else if (connectEvent != null)
            {
                previewLine.Draw(connectEvent.GetBottomPoint(), new Vector2(0f, 1f), mousePos, new Vector2(-1f, 0f));
            }
        }

        public bool TryCompleteConnectionToClip(TimelineClipBase targetClip)
        {
            if (!IsConnecting || targetClip == null) return false;
            if (connectClip != null)
            {
                if (targetClip == connectClip) { CancelConnect(); return true; }
                if (reverseConnect)
                    control.TrySetPreKey(connectClip, targetClip.Config.ClipKey);
                else
                    control.TrySetPreKey(targetClip, connectClip.Config.ClipKey);
                CancelConnect();
                return true;
            }
            if (connectEvent != null)
            {
                if (targetClip != connectEvent.Clip)
                    control.TrySetPreKey(targetClip, connectEvent.Config.EventKey);
                CancelConnect();
                return true;
            }
            return false;
        }

        public bool TryCompleteConnectionToNode(TimelineNodeBase node)
        {
            if (!IsConnecting || node == null) return false;
            if (connectClip != null && node.Clip != connectClip)
            {
                if (reverseConnect && node.Kind == TimelineNodeKind.End)
                {
                    control.TrySetPreKey(connectClip, node.Clip.Config.ClipKey);
                    CancelConnect();
                    return true;
                }
                if (!reverseConnect && node.Kind == TimelineNodeKind.Start)
                {
                    control.TrySetPreKey(node.Clip, connectClip.Config.ClipKey);
                    CancelConnect();
                    return true;
                }
            }
            if (connectEvent != null && node.Kind == TimelineNodeKind.Start && node.Clip != connectEvent.Clip)
            {
                control.TrySetPreKey(node.Clip, connectEvent.Config.EventKey);
                CancelConnect();
                return true;
            }
            return false;
        }

        public void ShowMainMenu(int rowIndex, float startTime)
        {
            CancelConnect();
            var menu = new GenericMenu();

            if (copiedClip != null)
            {
                menu.AddItem(new GUIContent("粘贴Clip"), false, () =>
                {
                    control.Timeline.StopPreView();
                    var clone = SkillTimelineJson.Deserialize<SkillClipConfig>(SkillTimelineJson.Serialize(copiedClip));
                    clone.ClipKey = control.GenerateUniqueClipKey(clone.ClipKey);
                    clone.PreKey = "";
                    clone.StartTime = Mathf.Max(0f, (float)control.Timeline.SnapToFrameIfRequired(startTime));
                    clone.DelayTime = 0f;
                    if (clone.EventConfigs != null)
                    {
                        foreach (var ev in clone.EventConfigs)
                            ev.EventKey = control.GenerateUniqueEventKey(ev.EventKey);
                    }
                    control.InsertClip(clone, rowIndex);
                });
                menu.AddSeparator("");
            }

            foreach (SkillClipType t in Enum.GetValues(typeof(SkillClipType)))
            {
                var captured = t;
                menu.AddItem(new GUIContent("添加Clip/" + TimelineClipBase.DisplayNameForType(captured)), false, () =>
                {
                    control.Timeline.StopPreView();
                    control.InsertNewClip(captured, rowIndex, Mathf.Max(0f, (float)control.Timeline.SnapToFrameIfRequired(startTime)));
                });
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("快捷键说明"), false, TimelineEditorShortcutControl.ShowHelpDialog);
            menu.ShowAsContext();
        }

        public void ShowClipMenu(TimelineClipBase clip)
        {
            CancelConnect();
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("复制Clip"), false, () =>
            {
                copiedClip = SkillTimelineJson.Deserialize<SkillClipConfig>(SkillTimelineJson.Serialize(clip.Config));
            });
            menu.AddItem(new GUIContent("添加事件"), false, () => control.AddEventToClip(clip));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("连接后继Clip"), false, () => { connectClip = clip; reverseConnect = false; });
            menu.AddItem(new GUIContent("连接前置Clip"), false, () => { connectClip = clip; reverseConnect = true; });
            if (!string.IsNullOrEmpty(clip.Config.PreKey))
                menu.AddItem(new GUIContent("断开前置连接"), false, () => control.TrySetPreKey(clip, ""));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("向上一层 (↑)"), false, () => control.ShortcutControl.MoveClipLayer(clip, -1));
            menu.AddItem(new GUIContent("向下一层 (↓)"), false, () => control.ShortcutControl.MoveClipLayer(clip, 1));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除Clip"), false, () => control.RemoveClip(clip));
            menu.ShowAsContext();
        }

        public void ShowNodeMenu(TimelineNodeBase node)
        {
            CancelConnect();
            var menu = new GenericMenu();
            if (node.Kind == TimelineNodeKind.Start)
            {
                menu.AddItem(new GUIContent("连接前置Clip"), false, () => { connectClip = node.Clip; reverseConnect = true; });
                if (!string.IsNullOrEmpty(node.Clip.Config.PreKey))
                    menu.AddItem(new GUIContent("断开前置连接"), false, () => control.TrySetPreKey(node.Clip, ""));
            }
            else
            {
                menu.AddItem(new GUIContent("连接后继Clip"), false, () => { connectClip = node.Clip; reverseConnect = false; });
                menu.AddItem(new GUIContent("断开所有后继"), false, () => control.DisconnectSuccessors(node.Clip.Config.ClipKey));
            }
            menu.ShowAsContext();
        }

        public void ShowEventMenu(TimelineEventBase ev)
        {
            CancelConnect();
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("连接Clip"), false, () => { connectEvent = ev; });
            menu.AddItem(new GUIContent("断开所有连接"), false, () => control.DisconnectSuccessors(ev.Config.EventKey));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除事件"), false, () => control.RemoveEvent(ev));
            menu.ShowAsContext();
        }
    }

    /// <summary>
    /// 像素级还原 Demo TimelineEditorShortcutControl：
    /// 拦截 Unity 的 ValidateCommand/ExecuteCommand("Save") 与 Ctrl+S，支持 ↑/↓ 换层、←/→ 按帧平移、Ctrl+←/→ 按帧序号整数调时长、Esc 取消。
    /// </summary>
    public sealed class TimelineEditorShortcutControl
    {
        public const string HelpText =
            "窗口有焦点时：\n" +
            "  Ctrl + S　　　　保存当前技能（文本框里也能存）\n" +
            "  Esc　　　　　　取消连线 / Catch\n" +
            "\n" +
            "画布操作：\n" +
            "  普通滚轮　　　　上下平移画布\n" +
            "  Ctrl + 滚轮　　 横向缩放时间轴\n" +
            "  Shift + 滚轮　　纵向缩放行高\n" +
            "  拖拽头尾蓝点　　直接拉出贝塞尔连线（近边缘自动滚屏）\n" +
            "  右键空白 / Clip　添加、复制、粘贴、加事件、连线、删除\n" +
            "\n" +
            "选中 Clip 后：\n" +
            "  ↑ / ↓　　　　　向上一层 / 向下一层\n" +
            "  ← / →　　　　　整体左移 / 右移（1 帧）\n" +
            "  Ctrl + ← / →　　时长变短 / 变长（1 帧）\n" +
            "  Delete　　　　　删除选中 Clip";

        private readonly TimelineControl control;
        private double lastSaveShortcutTime = -1d;

        public TimelineEditorShortcutControl(TimelineControl ctrl)
        {
            control = ctrl;
        }

        public static void ShowHelpDialog() =>
            EditorUtility.DisplayDialog("技能编辑器快捷键", HelpText, "知道了");

        public void Handle()
        {
            if (TryHandleSaveShortcut()) return;

            var evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown) return;

            if (evt.keyCode == KeyCode.Escape)
            {
                if (control.MenuControl.IsConnecting || control.IsNodeDragConnecting || control.HasPendingCatch)
                {
                    control.CancelAllPendingModes();
                    evt.Use();
                    control.Timeline.Repaint();
                }
                return;
            }

            if (EditorGUIUtility.editingTextField || evt.alt || evt.shift) return;

            var clip = control.SelectedClip;
            if (clip == null) return;

            if (evt.keyCode == KeyCode.Delete)
            {
                control.RemoveClip(clip);
                evt.Use();
                return;
            }

            bool withControl = evt.control || evt.command;
            bool handled = false;
            if (withControl)
            {
                if (evt.keyCode == KeyCode.LeftArrow) handled = clip.TryNudgeEditableDuration(control.Timeline._frameRate, -1);
                else if (evt.keyCode == KeyCode.RightArrow) handled = clip.TryNudgeEditableDuration(control.Timeline._frameRate, 1);
            }
            else
            {
                switch (evt.keyCode)
                {
                    case KeyCode.UpArrow: handled = MoveClipLayer(clip, -1); break;
                    case KeyCode.DownArrow: handled = MoveClipLayer(clip, 1); break;
                    case KeyCode.LeftArrow: handled = TryNudgeSelectedClipTime(clip, -1); break;
                    case KeyCode.RightArrow: handled = TryNudgeSelectedClipTime(clip, 1); break;
                }
            }

            if (handled)
            {
                evt.Use();
                control.MarkDirtyAndRebuild();
            }
        }

        public bool MoveClipLayer(TimelineClipBase clip, int delta)
        {
            int idx = control.Clips.IndexOf(clip);
            int dst = idx + delta;
            if (idx < 0 || dst < 0 || dst >= control.Clips.Count) return false;
            var list = control.Clips;
            var cfgList = control.Config.Clips;
            (list[idx], list[dst]) = (list[dst], list[idx]);
            (cfgList[idx], cfgList[dst]) = (cfgList[dst], cfgList[idx]);
            control.MarkDirtyAndRebuild();
            return true;
        }

        private bool TryNudgeSelectedClipTime(TimelineClipBase clip, int frameDelta)
        {
            float rate = control.Timeline._frameRate > 0f ? control.Timeline._frameRate : 30f;
            float step = 1f / rate * frameDelta;
            if (!string.IsNullOrEmpty(clip.Config.PreKey))
                clip.Config.DelayTime = Mathf.Max(0f, (float)control.Timeline.SnapToFrameIfRequired(clip.Config.DelayTime + step));
            else
                clip.Config.StartTime = Mathf.Max(0f, (float)control.Timeline.SnapToFrameIfRequired(clip.Config.StartTime + step));
            return true;
        }

        /// <summary>严格沿用 Demo：拦截 ValidateCommand/ExecuteCommand("Save") 与 KeyDown Ctrl+S，防止被 Unity 吃掉去保存场景。</summary>
        private bool TryHandleSaveShortcut()
        {
            var evt = Event.current;
            if (evt == null) return false;
            bool isSaveCommand = (evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand)
                && evt.commandName == "Save";
            bool isSaveKey = evt.type == EventType.KeyDown
                && evt.keyCode == KeyCode.S
                && (evt.control || evt.command)
                && !evt.alt
                && !evt.shift;
            if (!isSaveCommand && !isSaveKey) return false;

            bool validateOnly = evt.type == EventType.ValidateCommand;
            evt.Use();
            if (validateOnly) return true;

            double now = EditorApplication.timeSinceStartup;
            if (now - lastSaveShortcutTime < 0.3d) return true;
            lastSaveShortcutTime = now;
            control.Timeline.SaveCurrentTimeline();
            return true;
        }
    }
}
