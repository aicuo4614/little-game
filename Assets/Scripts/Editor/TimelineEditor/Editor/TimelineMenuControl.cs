using UnityEditor;
using UnityEngine;

public class TimelineMenuControl
{
    private TimelineControl control;
    private Vector2 lastLocalMousePosition;

    public TimelineMenuControl(TimelineControl control)
    {
        this.control = control;
    }

    public void Draw()
    {
        tempCount++;
        if (isShowClipMenu && menuClip != null && tempCount == 1)
        {
            OnShowClipMenu();
        }
        else if (isShowEventMenu && menuEvent != null && tempCount == 1)
        {
            OnShowEventMenu();
        }
        else if (isShowNodeMenu && menuNode != null && tempCount == 1)
        {
            OnShowNodeMenu();
        }

        if (this.control.MouseClickType == TimelineControl.MouseEventClickType.MouseRightUp)
        {
            if (this.control.SelectNothingInDrawRect)
            {
                OnShowMainMenu();
            }
        }

        if (this.control.SelectNothingInDrawRect)
        {
            isClipConnecting = false;
            isClipReverseConnecting = false;
            isEventConnecting = false;
        }

        ClipConnectLogic();
        EventConnectLogic();
    }

    private int tempCount;

    private TimelineClipBase menuClip;
    private bool isShowClipMenu;

    private TimelineEventBase menuEvent;
    private bool isShowEventMenu;

    private TimelineNodeBase menuNode;
    private bool isShowNodeMenu;

    private TimelineLineBase timelineLine = new TimelineLineBase();

    private bool isClipConnecting;
    public bool IsClipConnecting => isClipConnecting;

    /// <summary>从头节点发起连接：menuClip 为后继 Clip，点击前置 Clip/尾节点完成连接。</summary>
    private bool isClipReverseConnecting;
    public bool IsClipReverseConnecting => isClipReverseConnecting;

    private bool isEventConnecting;
    public bool IsEventConnecting => isEventConnecting;

    /// <summary>
    /// 跨技能粘贴剪贴板：static，避免「加载」重建 TimelineControl / MenuControl 后丢失。
    /// 内容为 JSON 深拷贝结果；粘贴时还会再拷一次再改 ClipKey。
    /// </summary>
    private static TimelineClipConfigBase copiedClipConfig;

    internal static bool HasCopiedClip => copiedClipConfig is not null;

    internal static void CopyClipConfig(TimelineClipConfigBase config)
    {
        copiedClipConfig = TimelineJsonUtil.FromTimelineClipConfigJson(TimelineJsonUtil.ToJson(config));
    }

    internal static TimelineClipConfigBase ReadCopiedClip()
    {
        return HasCopiedClip
            ? TimelineJsonUtil.FromTimelineClipConfigJson(TimelineJsonUtil.ToJson(copiedClipConfig))
            : null;
    }

    public void ShowClipMenu(TimelineClipBase clip)
    {
        menuClip = clip;
        isShowClipMenu = true;
        menuEvent = null;
        isShowEventMenu = false;
        menuNode = null;
        isShowNodeMenu = false;
        tempCount = 0;
    }

    public void ShowEventMenu(TimelineEventBase e)
    {
        menuClip = null;
        isShowClipMenu = false;
        menuEvent = e;
        isShowEventMenu = true;
        menuNode = null;
        isShowNodeMenu = false;
        tempCount = 0;
    }

    public void ShowNodeMenu(TimelineNodeBase node)
    {
        menuClip = null;
        isShowClipMenu = false;
        menuEvent = null;
        isShowEventMenu = false;
        menuNode = node;
        isShowNodeMenu = true;
        tempCount = 0;
    }

    private void OnShowMainMenu()
    {
        lastLocalMousePosition = Event.current.mousePosition;

        var menu = new GenericMenu();

        if (copiedClipConfig != null)
        {
            if (control.CanAddClip(copiedClipConfig.Type))
            {
                menu.AddItem(new GUIContent("粘贴Clip"), false, () =>
                {
                    control.Timeline.StopPreView();
                    var index = control.Timeline.GetIndexByMousePosition();
                    var startTime = Mathf.Max(0f, (float)control.Timeline.GetSnappedTimeAtMousePosition(control.Timeline.LastMousePosition));
                    control.InsertClipFromCopiedConfig(copiedClipConfig, index, startTime);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("粘贴Clip"));
            }

            menu.AddSeparator("");
        }

        var types = UnitFeatureTypeDisplay.Types;
        for (int i = 0; i < types.Count; i++)
        {
            var featureType = types[i];
            var path = UnitFeatureTypeDisplay.GetAddClipMenuPath(featureType);
            if (control.CanAddClip(featureType))
            {
                menu.AddItem(new GUIContent(path), false, () =>
                {
                    control.Timeline.StopPreView();
                    var index = this.control.Timeline.GetIndexByMousePosition();
                    var startTime = Mathf.Max(0f, (float)this.control.Timeline.GetSnappedTimeAtMousePosition(this.control.Timeline.LastMousePosition));
                    control.InsertEmptyClipAndInit(featureType, index, startTime);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(path));
            }
        }

        menu.ShowAsContext();
    }

    private void ClipConnectLogic()
    {
        if (!isClipConnecting || menuClip == null)
        {
            return;
        }

        if (isClipReverseConnecting)
        {
            timelineLine.Draw(menuClip.GetPreLinePoint(), new Vector2(-1, 0), Event.current.mousePosition, new Vector2(1, 0));
        }
        else
        {
            timelineLine.Draw(menuClip.GetEndLinePoint(), new Vector2(1, 0), Event.current.mousePosition, new Vector2(-1, 0));
        }
    }

    private void EventConnectLogic()
    {
        if (!isEventConnecting || menuEvent == null)
        {
            return;
        }
        timelineLine.Draw(menuEvent.GetBottomPoint(), new Vector2(0, 1), Event.current.mousePosition, new Vector2(-1, 0));
    }

    private void OnShowClipMenu()
    {
        lastLocalMousePosition = Event.current.mousePosition;
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent("添加自定义事件"), false, () =>
        {
            var config = new TimelineEventConfigBase()
            {
                EventKey = TimelineKeys.GenerateEventKey(),
                TimelineEventType = TimelineEventType.CustomEvent,
                ValueType = ValueType.Normal,
                Time = menuClip.GetOffsetXByMouse(this.control.Timeline.LastMousePosition) * menuClip.Config.Duration,
                EventName = "hit",
            };
            menuClip.Events.Add(menuClip.CreateEvent(config));
        });

        menu.AddItem(new GUIContent("向上一层\t↑"), false, () =>
        {
            if (menuClip != null)
            {
                control.ShortcutControl.MoveClipLayer(menuClip, -1);
            }
        });

        menu.AddItem(new GUIContent("向下一层\t↓"), false, () =>
        {
            if (menuClip != null)
            {
                control.ShortcutControl.MoveClipLayer(menuClip, 1);
            }
        });

        menu.AddItem(new GUIContent("复制Clip"), false, () =>
        {
            if (menuClip == null)
            {
                return;
            }

            menuClip.SyncEventConfigsBeforeSave();
            CopyClipConfig(menuClip.Config);
        });

        menu.AddItem(new GUIContent("移除"), false, () =>
        {
            TryRemoveMenuClip();
        });

        menu.ShowAsContext();
    }

    private void OnShowEventMenu()
    {
        lastLocalMousePosition = Event.current.mousePosition;
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent("连接"), false, () =>
        {
            isEventConnecting = true;
        });

        bool hasNext = !string.IsNullOrEmpty(menuEvent.Config.EventKey)
            && this.control.Clips.Exists(o => menuEvent.Config.EventKey.Equals(o.Config.PreKey));
        if (hasNext)
        {
            menu.AddItem(new GUIContent("断开"), false, () =>
            {
                OnEventDisconnectClip(menuEvent);
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("断开"));
        }

        if (menuEvent.Config.TimelineEventType == TimelineEventType.AnimationEvent)
        {
            menu.AddDisabledItem(new GUIContent("移除"));
        }
        else
        {
            menu.AddItem(new GUIContent("移除"), false, () =>
            {
                if (menuEvent != null)
                {
                    menuEvent.Clip.RemoveEvent(menuEvent);
                }
            });
        }

        if (menu.GetItemCount() > 0)
        {
            menu.ShowAsContext();
        }
    }

    private void OnShowNodeMenu()
    {
        if (menuNode == null)
        {
            return;
        }

        if (menuNode.Kind == TimelineNodeKind.End)
        {
            OnShowEndNodeMenu();
        }
        else
        {
            OnShowStartNodeMenu();
        }
    }

    /// <summary>尾节点：与 Clip 菜单一致，从当前 Clip 尾部拉出连接。</summary>
    private void OnShowEndNodeMenu()
    {
        lastLocalMousePosition = Event.current.mousePosition;
        var menu = new GenericMenu();
        var clip = menuNode.Clip;
        if (clip == null)
        {
            return;
        }

        menu.AddItem(new GUIContent("连接"), false, () =>
        {
            menuClip = clip;
            isClipConnecting = true;
            isClipReverseConnecting = false;
        });

        bool hasNext = !string.IsNullOrEmpty(clip.Config.ClipKey)
            && control.Clips.Exists(o => clip.Config.ClipKey.Equals(o.Config.PreKey));
        if (hasNext)
        {
            menu.AddItem(new GUIContent("断开"), false, () =>
            {
                OnClipDisconnectClip(clip);
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("断开"));
        }

        menu.ShowAsContext();
    }

    /// <summary>头节点：反向连接，从本 Clip 头部拉出连线，点击前置 Clip/尾节点完成。</summary>
    private void OnShowStartNodeMenu()
    {
        lastLocalMousePosition = Event.current.mousePosition;
        var menu = new GenericMenu();
        var clip = menuNode.Clip;
        if (clip == null)
        {
            return;
        }

        menu.AddItem(new GUIContent("连接"), false, () =>
        {
            menuClip = clip;
            isClipConnecting = true;
            isClipReverseConnecting = true;
        });

        bool hasPre = !string.IsNullOrEmpty(clip.Config.PreKey);
        if (hasPre)
        {
            menu.AddItem(new GUIContent("断开"), false, () =>
            {
                OnStartNodeDisconnectClip(clip);
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("断开"));
        }

        menu.ShowAsContext();
    }

    public void OnEventConnectClip(TimelineClipBase nextClip)
    {
        if (menuEvent == null || nextClip == null)
        {
            isEventConnecting = false;
            return;
        }

        if (menuEvent.Clip == nextClip)
        {
            isEventConnecting = false;
            return;
        }

        control.ConnectClipByEventPreKey(menuEvent, nextClip);
        isEventConnecting = false;
    }

    public void OnEventDisconnectClip(TimelineEventBase e)
    {
        var clips = this.control.Clips.FindAll(o => o.Config.PreKey.Equals(e.Config.EventKey));
        for (int i = 0; i < clips.Count; i++)
        {
            clips[i].Config.PreKey = "";
        }
    }

    public void OnClipConnectClip(TimelineClipBase otherClip)
    {
        if (menuClip == null || otherClip == null || menuClip == otherClip)
        {
            return;
        }

        if (isClipReverseConnecting)
        {
            control.ConnectClipsByPreKey(otherClip, menuClip);
        }
        else
        {
            control.ConnectClipsByPreKey(menuClip, otherClip);
        }

        FinishClipConnecting();
    }

    public void OnClipConnectEvent(TimelineEventBase fromEvent)
    {
        if (!isClipConnecting || !isClipReverseConnecting || menuClip == null || fromEvent == null)
        {
            return;
        }

        control.ConnectClipByEventPreKey(fromEvent, menuClip);
        FinishClipConnecting();
    }

    private void FinishClipConnecting()
    {
        isClipConnecting = false;
        isClipReverseConnecting = false;
    }

    private void OnClipDisconnectClip(TimelineClipBase preClip)
    {
        var clips = this.control.Clips.FindAll(o => o.Config.PreKey.Equals(preClip.Config.ClipKey));
        for (int i = 0; i < clips.Count; i++)
        {
            clips[i].Config.PreKey = "";
        }
        control.Timeline.Repaint();
    }

    private void OnStartNodeDisconnectClip(TimelineClipBase clip)
    {
        if (clip == null)
        {
            return;
        }
        clip.Config.PreKey = "";
        control.Timeline.Repaint();
    }

    private void TryRemoveMenuClip()
    {
        if (menuClip == null)
        {
            return;
        }

        if (menuClip.Config != null && menuClip.Config.Type == TimelineClipType.PlayAnimator)
        {
            if (!EditorUtility.DisplayDialog("移除 Clip", "确定要移除动画 Clip 吗？", "确定", "取消"))
            {
                return;
            }
        }

        this.control.Timeline.StopPreView();
        menuClip.OnDestroy();
        control.RemoveClip(menuClip);
        menuClip = null;
        control.Timeline.Repaint();
    }

    public void OnDestroy()
    {
    }
}
