using System;

/// <summary>隐藏战场 UI 的层（可多选）。GM 按钮不在此列，隐藏/还原时始终跟随。</summary>
[Flags]
public enum TimelineHideBattleUILayerType
{
    None = 0,
    /// <summary>顶部：TopBar + ToolPanel。</summary>
    Top = 1 << 0,
    /// <summary>底部：BottomBar（含技能 Tip）。</summary>
    Bottom = 1 << 1,
    /// <summary>飘血：FloatLayer。</summary>
    Float = 1 << 2,
    /// <summary>脚下血条：FootLayer。</summary>
    Foot = 1 << 3,
}

/// <summary>
/// 隐藏战场 UI。进入 Clip 时按 <see cref="HideLayers"/> 立即隐藏（瞬时，与施加力相同）。
/// 勾选 <see cref="ResetOnEnd"/> 时 Clip 结束还原所选层；默认不勾，保持隐藏直到时间轴 Start/Stop 全层还原。
/// GM 按钮始终隐藏/还原，不进多选。
/// </summary>
[Serializable]
public class TimelineClipHideBattleUIConfig : TimelineClipConfigBase
{
    /// <summary>默认全选：顶部、底部、飘血、脚下。</summary>
    public const TimelineHideBattleUILayerType DefaultHideLayers =
        TimelineHideBattleUILayerType.Top
        | TimelineHideBattleUILayerType.Bottom
        | TimelineHideBattleUILayerType.Float
        | TimelineHideBattleUILayerType.Foot;

    /// <summary>要隐藏的层（可多选）。旧 JSON 缺字段时为全选。</summary>
    public TimelineHideBattleUILayerType HideLayers = DefaultHideLayers;

    /// <summary>
    /// 勾选后 Clip 结束时还原本 Clip 隐藏的层；默认不勾，结束后保持隐藏。
    /// </summary>
    public bool ResetOnEnd;
}
