using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器：隐藏战场 UI。瞬时 Clip（与施加力相同，时长锁定 0）。
/// 美术工程无战斗 UI，预览无实操；共享播放器调用程序宿主按 HideLayers 隐藏。
/// 勾选「结束时重置」时 Clip 结束还原所选层；默认不勾，保持隐藏直到时间轴 Start/Stop。
/// GM 始终隐藏，不进多选。
/// </summary>
public class TimelineClipHideBattleUI : TimelineClipBase
{
    private TimelineClipHideBattleUIConfig thisConfig;

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipHideBattleUIConfig;
        base.Init(control, config);
    }

    public override void Draw()
    {
        this.config.Duration = 0;
        base.Draw();
    }

    /// <summary>瞬时 Clip，无时长可调。</summary>
    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        return false;
    }

    protected override void OnPass(bool leftToRight)
    {
        this.config.Duration = 0;
        base.OnPass(leftToRight);
    }

    public override void DrawInspector()
    {
        base.DrawInspector();
        EditorGUILayout.HelpBox(
            "瞬时隐藏战场 UI（与施加力相同，条长不可调）。\n" +
            "勾选「结束时重置」则 Clip 结束时还原所选层；默认不勾选，保持隐藏直到时间轴结束。\n" +
            "技能编辑器内无战斗 UI，本 Clip 仅配置；效果在程序工程运行时生效。\n" +
            "GM 按钮始终隐藏，不在下列多选中。",
            MessageType.Info);

        EditorGUILayout.LabelField("隐藏层（多选）");
        EditorGUI.indentLevel++;
        var next = thisConfig.HideLayers;
        next = DrawLayerToggle(next, TimelineHideBattleUILayerType.Top, "顶部（TopBar ToolPanel）");
        next = DrawLayerToggle(next, TimelineHideBattleUILayerType.Bottom, "底部（BottomBar）");
        next = DrawLayerToggle(next, TimelineHideBattleUILayerType.Float, "飘血（FloatLayer）");
        next = DrawLayerToggle(next, TimelineHideBattleUILayerType.Foot, "脚底（FootLayer）");
        EditorGUI.indentLevel--;
        thisConfig.HideLayers = next;

        thisConfig.ResetOnEnd = EditorGUILayout.Toggle("结束时重置", thisConfig.ResetOnEnd);
    }

    private static TimelineHideBattleUILayerType DrawLayerToggle(
        TimelineHideBattleUILayerType current,
        TimelineHideBattleUILayerType flag,
        string label)
    {
        var on = (current & flag) != 0;
        var nextOn = EditorGUILayout.ToggleLeft(label, on);
        if (nextOn == on)
        {
            return current;
        }

        if (nextOn)
        {
            return current | flag;
        }

        return current & ~flag;
    }

    protected override void SetRect()
    {
        var y = this.control.Timeline.GetYByIndex(this.control.Clips.IndexOf(this));
        rect.x = control.Timeline.WorldPixelXToLocal(control.Timeline.TimeToPixel(config.StartTime + config.DelayTime));
        rect.y = control.Timeline.WorldPixelYToLocal(control.Timeline.YToPixel(y)) + paddingRate / 2.0f * control.Timeline.GetItemPixelHeight();

        rect.width = control.Timeline.TimeToPixel(config.StartTime + 0.4f) - control.Timeline.TimeToPixel(config.StartTime);
        rect.height = height;
    }
}
