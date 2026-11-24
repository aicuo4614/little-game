using LittleGame.Client.SkillTimeline;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor.Timeline
{
    public enum TimelineNodeKind
    {
        Start = 0,
        End = 1
    }

    /// <summary>像素级沿用 Demo TimelineNodeBase：半径 4px 亮蓝圆点，支持直接左键按住拖出连线与右键菜单。</summary>
    public sealed class TimelineNodeBase
    {
        private const float Radius = 4f;
        private const float HitRadius = 6.5f;
        private const float SelectedRingRadius = 7f;
        private static readonly Color NodeFillColor = new Color(0.55f, 0.88f, 1f, 1f);
        private static readonly Color NodeBorderColor = new Color(0.2f, 0.45f, 0.65f, 1f);
        private static readonly Color SelectedRingColor = new Color(1f, 0.92f, 0.35f, 1f);

        private TimelineControl control;
        public TimelineClipBase Clip { get; private set; }
        public TimelineNodeKind Kind { get; private set; }

        public void Init(TimelineControl ctrl, TimelineClipBase owner, TimelineNodeKind kind)
        {
            control = ctrl;
            Clip = owner;
            Kind = kind;
        }

        public Vector2 GetCenter() => Clip == null
            ? Vector2.zero
            : (Kind == TimelineNodeKind.Start ? Clip.GetPreLinePoint() : Clip.GetEndLinePoint());

        public bool Contains(Vector2 localPos) => (localPos - GetCenter()).sqrMagnitude <= HitRadius * HitRadius;

        public bool ShouldDraw(Vector2 mousePos)
        {
            if (Clip == null || control == null) return false;
            if (control.SelectedNode == this || control.IsNodeDragConnecting || control.MenuControl.IsConnecting) return true;
            if (Contains(mousePos) || Clip.Rect.Contains(mousePos)) return true;
            return HasConnection();
        }

        public bool HasConnection()
        {
            if (Clip?.Config == null) return false;
            if (Kind == TimelineNodeKind.Start) return !string.IsNullOrEmpty(Clip.Config.PreKey);
            return control.Clips.Exists(c => c.Config != null && c.Config.PreKey == Clip.Config.ClipKey);
        }

        public void Draw(Vector2 mousePos)
        {
            if (!ShouldDraw(mousePos)) return;
            Vector2 center = GetCenter();
            Handles.BeginGUI();
            Color prev = Handles.color;
            if (control.SelectedNode == this)
            {
                Handles.color = SelectedRingColor;
                Handles.DrawSolidDisc(center, Vector3.forward, SelectedRingRadius);
            }
            Handles.color = NodeBorderColor;
            Handles.DrawSolidDisc(center, Vector3.forward, Radius + 1f);
            Handles.color = NodeFillColor;
            Handles.DrawSolidDisc(center, Vector3.forward, Radius);
            Handles.color = prev;
            Handles.EndGUI();
        }
    }

    /// <summary>像素级沿用 Demo TimelineEventBase：5×6.5px 色块、浅灰事件名、选中黄框与底部蓝色贝塞尔连线。</summary>
    public sealed class TimelineEventBase
    {
        private const float RectWidth = 5f;
        private const float RectHeight = 6.5f;
        private static readonly Color EventColor = new Color(1f, 0.78f, 0.22f, 1f);
        private static readonly Color BorderColor = new Color(1f, 0.92f, 0.35f, 1f);
        private static readonly Color EventLabelTextColor = new Color(0xA3 / 255f, 0xA3 / 255f, 0xA3 / 255f, 1f);
        private static GUIStyle eventLabelStyle;

        private TimelineControl control;
        private readonly TimelineLineBase line = new TimelineLineBase();
        public TimelineClipBase Clip { get; private set; }
        public SkillEventConfig Config { get; private set; }
        public Rect Rect { get; private set; }

        public void Init(TimelineControl ctrl, TimelineClipBase owner, SkillEventConfig cfg)
        {
            control = ctrl;
            Clip = owner;
            Config = cfg;
        }

        public Vector2 GetBottomPoint() => new Vector2(Rect.center.x, Rect.yMax);

        public float GetEventTimelineTime()
        {
            float clipStart = control.ResolveClipStart(Clip.Config);
            float offset = Clip.Config.Type == SkillClipType.PlayAnimation && Clip.Config.Speed > 0.001f
                ? Config.Time / Clip.Config.Speed
                : Config.Time;
            return clipStart + offset;
        }

        public void Draw()
        {
            float x = control.TimeToLocalX(GetEventTimelineTime());
            float y = Clip.Rect.yMax - RectHeight - 1f;
            Rect = new Rect(x - RectWidth * 0.5f, y, RectWidth, RectHeight);

            EditorGUI.DrawRect(Rect, EventColor);
            if (control.SelectedEvent == this)
                TimelineClipBase.DrawBorderRect(Rect, BorderColor, 2f);

            if (!string.IsNullOrEmpty(Config.EventName))
            {
                if (eventLabelStyle == null)
                {
                    eventLabelStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
                    eventLabelStyle.normal.textColor = EventLabelTextColor;
                }
                GUI.Label(new Rect(Rect.xMax + 2f, Rect.y - 4f, 90f, 14f), Config.EventName, eventLabelStyle);
            }

            for (int i = 0; i < control.Clips.Count; i++)
            {
                var target = control.Clips[i];
                if (target.Config != null && target.Config.PreKey == Config.EventKey)
                {
                    line.Draw(GetBottomPoint(), new Vector2(0f, 1f), target.GetPreLinePoint(), new Vector2(-1f, 0f));
                }
            }
        }

        public void DrawInspector()
        {
            EditorGUILayout.LabelField("Event", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("EventKey", Config.EventKey ?? string.Empty);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginChangeCheck();
            Config.EventName = EditorGUILayout.TextField("事件名", Config.EventName ?? string.Empty);
            float maxT = Clip.Config.Type == SkillClipType.PlayAnimation
                ? Clip.Config.Duration * Mathf.Max(0.1f, Clip.Config.Speed)
                : Clip.Config.Duration;
            Config.Time = Mathf.Clamp(EditorGUILayout.FloatField("时间", Config.Time), 0f, Mathf.Max(0f, maxT));
            if (EditorGUI.EndChangeCheck())
                control.MarkDirtyAndRebuild();

            EditorGUILayout.Space(4);
        }
    }
}
