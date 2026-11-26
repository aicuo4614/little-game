using UnityEditor;
using UnityEngine;

public enum TimelineNodeKind
{
    Start = 0,
    End = 1,
}

/// <summary>Clip 头尾连接点，绘制在条块前方/后方中心。</summary>
public class TimelineNodeBase
{
    private const float Radius = 4f;
    private const float HitRadius = 6f;
    private const float SelectedRingRadius = 7f;
    private static readonly Color NodeFillColor = new Color(0.55f, 0.88f, 1f, 1f);
    private static readonly Color NodeBorderColor = new Color(0.2f, 0.45f, 0.65f, 1f);
    private static readonly Color SelectedRingColor = new Color(1f, 0.92f, 0.35f, 1f);

    private TimelineControl control;
    private TimelineClipBase clip;
    private TimelineNodeKind kind;
    private NodeOperateStateType curNodeOperateStateType;

    public TimelineNodeKind Kind => kind;
    public TimelineClipBase Clip => clip;
    public NodeOperateStateType CurNodeOperateStateType => curNodeOperateStateType;

    public enum NodeOperateStateType
    {
        Normal = 0,
        Selected,
        Drag,
    }

    public void Init(TimelineControl control, TimelineClipBase clip, TimelineNodeKind kind)
    {
        this.control = control;
        this.clip = clip;
        this.kind = kind;
        ChangeNodeOperateStateType(NodeOperateStateType.Normal);
    }

    public Vector2 GetCenter()
    {
        if (clip == null)
        {
            return Vector2.zero;
        }
        return kind == TimelineNodeKind.Start ? clip.GetPreLinePoint() : clip.GetEndLinePoint();
    }

    /// <summary>鼠标是否在节点命中范围内（与 GetCenter 同为 BeginClip(drawRect) 内局部坐标）。</summary>
    public bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (clip == null)
        {
            return false;
        }
        Vector2 center = GetCenter();
        return (screenPoint - center).sqrMagnitude <= HitRadius * HitRadius;
    }

    /// <summary>
    /// 鼠标在附着 Clip 内、在节点命中范围内、或该节点已有连线时显示；
    /// 选中/拖拽中的节点始终显示。
    /// </summary>
    public bool ShouldDraw()
    {
        if (clip == null || control == null)
        {
            return false;
        }

        if (curNodeOperateStateType != NodeOperateStateType.Normal || control.SelectNode == this)
        {
            return true;
        }

        var mousePos = Event.current.mousePosition;
        if (ContainsScreenPoint(mousePos))
        {
            return true;
        }

        if (control.Contains(clip.Rect, mousePos))
        {
            return true;
        }

        return HasConnection();
    }

    private bool HasConnection()
    {
        if (clip?.Config == null)
        {
            return false;
        }

        if (kind == TimelineNodeKind.Start)
        {
            return !string.IsNullOrEmpty(clip.Config.PreKey);
        }

        var clipKey = clip.Config.ClipKey;
        if (string.IsNullOrEmpty(clipKey))
        {
            return false;
        }

        var clips = control.Clips;
        for (int i = 0; i < clips.Count; i++)
        {
            var otherConfig = clips[i].Config;
            if (otherConfig != null && clipKey == otherConfig.PreKey)
            {
                return true;
            }
        }

        return false;
    }

    public void Draw()
    {
        if (clip == null || !ShouldDraw())
        {
            return;
        }
        DrawCircle(GetCenter(), Radius, NodeFillColor, NodeBorderColor);
        NodeOperateStateTypeLogic();
    }

    #region state

    private void NodeOperateStateTypeLogic()
    {
        switch (curNodeOperateStateType)
        {
            case NodeOperateStateType.Normal:
                if (control.SelectNode == this)
                {
                    if (TryAcceptEventConnecting())
                    {
                        break;
                    }
                    if (TryAcceptClipConnecting())
                    {
                        break;
                    }
                    control.OnNodeSelect(this);
                }
                break;
            case NodeOperateStateType.Selected:
                SelectedStateLogic();
                break;
            case NodeOperateStateType.Drag:
                DragStateLogic();
                break;
            default:
                break;
        }
    }

    private void SelectedStateLogic()
    {
        DrawSelectionRing(GetCenter());

        if (control.SelectNode == this && TryAcceptEventConnecting())
        {
            return;
        }

        if (control.MouseClickType == TimelineControl.MouseEventClickType.MouseRightUp
            && control.SelectNode == this)
        {
            control.MenuControl.ShowNodeMenu(this);
        }

        if (ShouldDeselectOnClick())
        {
            ChangeNodeOperateStateType(NodeOperateStateType.Normal);
            return;
        }

        if (control.SelectNothingInDrawRect)
        {
            ChangeNodeOperateStateType(NodeOperateStateType.Normal);
            return;
        }

        if (ShouldBeginDragConnect())
        {
            ChangeNodeOperateStateType(NodeOperateStateType.Drag);
        }
    }

    private void DragStateLogic()
    {
        DrawSelectionRing(GetCenter());
        if (control.MouseClickType != TimelineControl.MouseEventClickType.MouseLeftUp)
        {
            return;
        }
        control.TryCompleteNodeDragConnect(this);
        ChangeNodeOperateStateType(NodeOperateStateType.Selected);
    }

    private bool ShouldDeselectOnClick()
    {
        if (control.SelectNode == this)
        {
            return false;
        }
        return control.MouseClickType == TimelineControl.MouseEventClickType.MouseLeftDown
            || control.MouseClickType == TimelineControl.MouseEventClickType.MouseRightUp;
    }

    private bool TryAcceptEventConnecting()
    {
        if (clip == null || kind != TimelineNodeKind.Start || !control.MenuControl.IsEventConnecting)
        {
            return false;
        }

        control.MenuControl.OnEventConnectClip(clip);
        return true;
    }

    private bool TryAcceptClipConnecting()
    {
        if (clip == null || !control.MenuControl.IsClipConnecting)
        {
            return false;
        }

        if (kind == TimelineNodeKind.Start)
        {
            if (control.MenuControl.IsClipReverseConnecting)
            {
                return false;
            }
            control.MenuControl.OnClipConnectClip(clip);
            return true;
        }

        if (kind == TimelineNodeKind.End)
        {
            if (!control.MenuControl.IsClipReverseConnecting)
            {
                return false;
            }
            control.MenuControl.OnClipConnectClip(clip);
            return true;
        }

        return false;
    }

    private bool ShouldBeginDragConnect()
    {
        if (kind != TimelineNodeKind.End && kind != TimelineNodeKind.Start)
        {
            return false;
        }
        Event evt = Event.current;
        if (evt == null)
        {
            return false;
        }
        if (evt.button != 0)
        {
            return false;
        }
        if (evt.type != EventType.MouseDown && evt.type != EventType.MouseDrag)
        {
            return false;
        }
        return ContainsScreenPoint(evt.mousePosition);
    }

    public void ChangeNodeOperateStateType(NodeOperateStateType type)
    {
        if (curNodeOperateStateType == type)
        {
            return;
        }

        if (curNodeOperateStateType == NodeOperateStateType.Drag)
        {
            control.ClearNodeDragConnectSource();
        }

        curNodeOperateStateType = type;
        control.NotifySelectionChanged();
        if (curNodeOperateStateType == NodeOperateStateType.Drag)
        {
            control.SetNodeDragConnectSource(this);
        }
    }

    private static void DrawSelectionRing(Vector2 center)
    {
        Handles.BeginGUI();
        Color prevColor = Handles.color;
        Handles.color = SelectedRingColor;
        Handles.DrawWireDisc(center, Vector3.forward, SelectedRingRadius);
        Handles.color = prevColor;
        Handles.EndGUI();
    }

    #endregion

    private static void DrawCircle(Vector2 center, float radius, Color fillColor, Color borderColor)
    {
        Handles.BeginGUI();
        Color prevColor = Handles.color;
        Handles.color = fillColor;
        Handles.DrawSolidDisc(center, Vector3.forward, radius);
        Handles.color = borderColor;
        Handles.DrawWireDisc(center, Vector3.forward, radius);
        Handles.color = prevColor;
        Handles.EndGUI();
    }
}
