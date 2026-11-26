using UnityEditor;
using UnityEngine;

/// <summary>
/// Timeline 编辑器快捷键集中处理。
/// ↑/↓：选中 Clip 上下换层；←/→：整体左右平移；Ctrl+←/→：缩短/加长可编辑时长；Ctrl+S：保存。
/// </summary>
public class TimelineEditorShortcutControl
{
    /// <summary>供工具栏弹窗与右键菜单展示的快捷键说明。</summary>
    public const string HelpText =
        "窗口有焦点时：\n" +
        "  Ctrl + S　　　　保存当前技能（文本框里也能存）\n" +
        "\n" +
        "选中 Clip 后：\n" +
        "  ↑ / ↓　　　　　向上一层 / 向下一层\n" +
        "  ← / →　　　　　整体左移 / 右移（1 帧）\n" +
        "  Ctrl + ← / →　　时长变短 / 变长（1 帧）\n" +
        "\n" +
        "说明：\n" +
        "  · 有前置 PreKey 时，←/→ 调整延迟时间\n" +
        "  · 无 PreKey 时，←/→ 调整开始时间\n" +
        "  · Ctrl+←/→ 改的是各 Clip「可编辑时长」\n" +
        "    （如 Move 逻辑时长、Translate 时长参数）\n" +
        "  · 以下类型无法改时长：Catch 绑定、\n" +
        "    纯动画自动、子弹飞行、瞬时 Clip\n" +
        "  · 在文本输入框中编辑时，方向键快捷键无效";

    private TimelineControl control;
    private double lastSaveShortcutTime = -1;

    public TimelineEditorShortcutControl(TimelineControl control)
    {
        this.control = control;
    }

    /// <summary>弹出快捷键说明对话框，供操作者查阅。</summary>
    public static void ShowHelpDialog()
    {
        EditorUtility.DisplayDialog("技能编辑器快捷键", HelpText, "知道了");
    }

    public void Handle()
    {
        if (TryHandleSaveShortcut())
        {
            return;
        }

        if (!TryGetArrowKeyDown(out var keyCode, out var withControl))
        {
            return;
        }

        var handled = false;
        if (withControl)
        {
            switch (keyCode)
            {
                case KeyCode.LeftArrow:
                    handled = TryNudgeSelectedClipDuration(-1);
                    break;
                case KeyCode.RightArrow:
                    handled = TryNudgeSelectedClipDuration(1);
                    break;
            }
        }
        else
        {
            switch (keyCode)
            {
                case KeyCode.UpArrow:
                    handled = TryMoveSelectedClipLayer(-1);
                    break;
                case KeyCode.DownArrow:
                    handled = TryMoveSelectedClipLayer(1);
                    break;
                case KeyCode.LeftArrow:
                    handled = TryNudgeSelectedClipTime(-1);
                    break;
                case KeyCode.RightArrow:
                    handled = TryNudgeSelectedClipTime(1);
                    break;
            }
        }

        if (handled)
        {
            Event.current.Use();
            control.Timeline?.RefreshUnsavedChangesFlag();
            control.Timeline?.Repaint();
        }
    }

    /// <summary>交换 Clip 在列表中的层级，delta=-1 向上一层，delta=1 向下一层。</summary>
    public bool MoveClipLayer(TimelineClipBase clip, int delta)
    {
        var clips = control.Clips;
        if (clip == null || delta == 0 || clips == null || clips.Count <= 1)
        {
            return false;
        }

        var index = clips.IndexOf(clip);
        if (index < 0)
        {
            return false;
        }

        var targetIndex = index + delta;
        if (targetIndex < 0 || targetIndex >= clips.Count)
        {
            return false;
        }

        var temp = clips[index];
        clips[index] = clips[targetIndex];
        clips[targetIndex] = temp;
        return true;
    }

    private bool TryMoveSelectedClipLayer(int delta)
    {
        var clip = control.GetSelectedClip();
        return clip != null && MoveClipLayer(clip, delta);
    }

    /// <summary>按帧平移 Clip：有 PreKey 调 DelayTime，否则调 StartTime。frameDelta 为帧数（负=左）。</summary>
    private bool TryNudgeSelectedClipTime(int frameDelta)
    {
        var clip = control.GetSelectedClip();
        if (clip == null || frameDelta == 0)
        {
            return false;
        }

        var config = clip.Config;
        var step = GetFrameStep() * frameDelta;
        if (!string.IsNullOrEmpty(config.PreKey))
        {
            var next = config.DelayTime + step;
            config.DelayTime = Mathf.Max(0f, (float)control.Timeline.SnapToFrameIfRequired(next));
        }
        else
        {
            var next = config.StartTime + step;
            config.StartTime = Mathf.Max(0f, (float)control.Timeline.SnapToFrameIfRequired(next));
        }

        return true;
    }

    /// <summary>按帧增减可编辑时长，委托各 Clip 自己处理真实写入字段。</summary>
    private bool TryNudgeSelectedClipDuration(int frameDelta)
    {
        var clip = control.GetSelectedClip();
        if (clip == null || frameDelta == 0)
        {
            return false;
        }

        return clip.TryNudgeEditableDuration(GetFrameStep(), frameDelta);
    }

    private float GetFrameStep()
    {
        var rate = control.Timeline._frameRate;
        if (rate <= 0f)
        {
            return 1f / 30f;
        }

        return 1f / rate;
    }

    /// <summary>窗口有焦点时 Ctrl/Cmd+S 覆盖保存，并拦住 Unity 默认的保存场景。按住不放只存一次。</summary>
    private bool TryHandleSaveShortcut()
    {
        var evt = Event.current;
        var isSaveCommand = (evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand)
            && evt.commandName == "Save";
        var isSaveKey = evt.type == EventType.KeyDown
            && evt.keyCode == KeyCode.S
            && (evt.control || evt.command)
            && !evt.alt
            && !evt.shift;
        if (!isSaveCommand && !isSaveKey)
        {
            return false;
        }

        var validateOnly = evt.type == EventType.ValidateCommand;
        evt.Use();
        if (validateOnly)
        {
            return true;
        }

        var now = EditorApplication.timeSinceStartup;
        if (now - lastSaveShortcutTime < 0.3)
        {
            return true;
        }

        lastSaveShortcutTime = now;
        control.Timeline?.SaveCurrentTimeline();
        return true;
    }

    /// <summary>读取方向键；withControl 表示是否按住 Ctrl/Cmd（不含 Alt/Shift）。</summary>
    private static bool TryGetArrowKeyDown(out KeyCode keyCode, out bool withControl)
    {
        keyCode = KeyCode.None;
        withControl = false;
        var evt = Event.current;
        if (evt.type != EventType.KeyDown || evt.alt || evt.shift)
        {
            return false;
        }

        if (EditorGUIUtility.editingTextField)
        {
            return false;
        }

        keyCode = evt.keyCode;
        if (keyCode != KeyCode.UpArrow
            && keyCode != KeyCode.DownArrow
            && keyCode != KeyCode.LeftArrow
            && keyCode != KeyCode.RightArrow)
        {
            return false;
        }

        withControl = evt.control || evt.command;
        return true;
    }
}
