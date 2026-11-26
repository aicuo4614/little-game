using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static TimelineClipBase;

public class TimelineEventBase
{
    private float rectWidth = 5;
    private float rectHeight = 6.5f;

    private static Texture2D eventIcon;

    private TimelineControl control;
    private TimelineClipBase clip;
    public TimelineClipBase Clip => clip;
    private TimelineEventConfigBase config;
    public TimelineEventConfigBase Config => config;

    private List<TimelineLineBase> lines = new List<TimelineLineBase>();

    private Rect rect = new Rect();
    public Rect Rect => rect;
    private Color borderColor = new Color(1f, 0.92f, 0.35f, 1f);

    private float borderWidth = 2f;

    private static GUIStyle eventLabelStyle;
    private static readonly Color EventLabelTextColor = new Color(0xA3 / 255f, 0xA3 / 255f, 0xA3 / 255f, 1f);

    public void Init(TimelineControl control, TimelineClipBase clip, TimelineEventConfigBase config)
    {
        this.control = control;
        this.clip = clip;
        this.config = config;
        ChangeClipOperateStateType(ClipOperateStateType.Normal);
    }

    public void Draw()
    {
        rect = GetRect();
        EditorGUI.DrawRect(rect, GetColorByEventType());
        DrawEventNameLabel();

        ClipOperateStateTypeLogic();

        //查找到需要连接到的clips
        var clips = this.control.Clips.FindAll(o => o?.Config != null && string.Equals(o.Config.PreKey, this.config.EventKey, System.StringComparison.Ordinal));

        if (this.lines.Count < clips.Count)
        {
            var needCount = clips.Count - this.lines.Count;
            for (int i = 0; i < needCount; i++)
            {
                this.lines.Add(new TimelineLineBase());
            }
        }
        for (int i = 0; i < clips.Count; i++)
        {
            var toClip = clips[i];
            if (toClip != null)
            {
                this.lines[i].Draw(GetBottomPoint(), new Vector2(0, 1), toClip.GetPreLinePoint(), new Vector2(-1, 0));
            }
        }
    }

    public void DrawInspector()
    {
        var isAnimationEvent = config.TimelineEventType == TimelineEventType.AnimationEvent;
        var maxTime = clip.Config != null ? clip.Config.Duration : 0f;
        if (clip.Config is TimelineClipPlaySpineConfig spine)
        {
            maxTime *= spine.Speed;
        }

        EditorGUILayout.LabelField("Event", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(isAnimationEvent);
        config.TimelineEventType = TimelineEnumDisplay.DrawEventPopup("类型", config.TimelineEventType);
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("EventKey", config.EventKey ?? string.Empty);
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(isAnimationEvent);
        var nextEventName = EditorGUILayout.TextField("事件名", config.EventName ?? string.Empty);
        EditorGUI.EndDisabledGroup();
        if (!isAnimationEvent && nextEventName != (config.EventName ?? string.Empty))
        {
            config.EventName = nextEventName;
        }

        EditorGUI.BeginDisabledGroup(isAnimationEvent);
        config.ValueType = TimelineEnumDisplay.DrawValuePopup("时间类型", config.ValueType);
        EditorGUI.EndDisabledGroup();

        var timeLabel = config.ValueType == ValueType.Percent ? "时间(0~1)" : "时间";
        var timeMax = config.ValueType == ValueType.Percent ? 1f : maxTime;
        EditorGUI.BeginChangeCheck();
        EditorGUI.BeginDisabledGroup(isAnimationEvent);
        config.Time = EditorGUILayout.FloatField(timeLabel, config.Time);
        EditorGUI.EndDisabledGroup();
        if (EditorGUI.EndChangeCheck())
        {
            config.ClampStoredTime(maxTime);
            control.Timeline.Repaint();
        }

        EditorGUILayout.Space(4);
    }


    #region state

    private float dragPickMouseXOffset;

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

                if (this.control.SelectEvent == this)
                {
                    this.control.OnEventSelect(this);
                }
                break;
            case ClipOperateStateType.Selected:
                DrawBorder(rect, borderColor, borderWidth);

                if (this.control.MouseClickType == TimelineControl.MouseEventClickType.MouseRightUp && this.control.SelectEvent == this)
                {
                    this.control.MenuControl.ShowEventMenu(this);
                }

                Event evt = Event.current;
                if (evt != null && (evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag) && evt.button == 0 && this.control.Contains(rect, evt.mousePosition))
                {
                    if (this.config.TimelineEventType != TimelineEventType.AnimationEvent)
                    {
                        ChangeClipOperateStateType(ClipOperateStateType.Drag);
                        return;
                    }
                }
                if (
                    (this.control.MouseClickType == TimelineControl.MouseEventClickType.MouseLeftDown
                    || this.control.MouseClickType == TimelineControl.MouseEventClickType.MouseRightUp
                    )
                    && control.SelectEvent != this)
                {
                    ChangeClipOperateStateType(ClipOperateStateType.Normal);
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

                DrawBorder(rect, borderColor, borderWidth);
                ApplyClipStartTimeFromMouseX(Event.current.mousePosition.x);

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

                if (this.control.SelectEvent == this
                    && this.control.MenuControl.IsClipConnecting
                    && this.control.MenuControl.IsClipReverseConnecting)
                {
                    this.control.MenuControl.OnClipConnectEvent(this);
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

    private float GetAbsoluteTimeInClip()
    {
        return TimelineSchedule.GetEventOffset(clip.Config, config);
    }

    private float GetEventTimelineTime()
    {
        return clip.GetContentTimelineStartTime() + GetAbsoluteTimeInClip();
    }

    private void BeginDragPickOffset(float mouseX)
    {
        dragPickMouseXOffset = mouseX - control.Timeline.TimeToPixel(GetEventTimelineTime());
    }

    private void ApplyClipStartTimeFromMouseX(float mouseX)
    {
        var timelineTime = control.Timeline.PixelToTime(mouseX - dragPickMouseXOffset);
        timelineTime = (float)control.Timeline.SnapToFrameIfRequired(timelineTime);
        var absoluteInClip = timelineTime - clip.GetContentTimelineStartTime();
        var duration = clip.Config.Duration;
        if (clip.Config is TimelineClipPlaySpineConfig spine && config.ValueType != ValueType.Percent)
        {
            absoluteInClip *= spine.Speed;
            duration *= spine.Speed;
        }
        config.SetAbsoluteTime(absoluteInClip, duration);
        control.SharedPlayback.Invalidate();
        control.Timeline.Repaint();
    }

    private void DrawBorder(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    #endregion


    public Vector2 GetCenterPoint()
    {
        return rect.center;
    }

    public Vector2 GetBottomPoint()
    {
        return new Vector2(rect.center.x, rect.yMax);
    }

    private Rect GetRect()
    {
        rect.x = this.control.Timeline.WorldPixelXToLocal(this.control.Timeline.TimeToPixel(GetEventTimelineTime()));
        rect.y = this.clip.Rect.yMax - this.rectHeight;
        rect.width = this.rectWidth;
        rect.height = this.rectHeight;
        return rect;
    }

    private void DrawEventNameLabel()
    {
        if (config == null || rect.height <= 0f)
        {
            return;
        }

        if (eventLabelStyle == null)
        {
            eventLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.LowerCenter,
                clipping = TextClipping.Clip,
                wordWrap = false,
            };
            ApplyEventLabelTextColor(eventLabelStyle, EventLabelTextColor);
        }

        bool isNoname = string.IsNullOrEmpty(config.EventName);
        var label = isNoname ? "noname" : config.EventName;
        const float labelGap = 2f;
        float labelHeight = eventLabelStyle.lineHeight;
        float textWidth = eventLabelStyle.CalcSize(new GUIContent(label)).x;
        var labelRect = new Rect(
            rect.center.x - textWidth * 0.5f,
            rect.y - labelHeight - labelGap,
            textWidth,
            labelHeight);

        ApplyEventLabelTextColor(eventLabelStyle, isNoname ? Color.red : EventLabelTextColor);
        GUI.Label(labelRect, label, eventLabelStyle);
    }

    private static void ApplyEventLabelTextColor(GUIStyle style, Color color)
    {
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.focused.textColor = color;
        style.onNormal.textColor = color;
        style.onHover.textColor = color;
        style.onActive.textColor = color;
        style.onFocused.textColor = color;
    }

    private Color GetColorByEventType()
    {
        if (this.config.TimelineEventType == TimelineEventType.AnimationEvent)
        {
            return new Color(0.32f, 0.32f, 0.32f, 1f);
        }
        else if (this.config.TimelineEventType == TimelineEventType.CustomEvent)
        {
            return new Color(1f, 1f, 1f, 1f);
        }
        return Color.white;
    }
}
