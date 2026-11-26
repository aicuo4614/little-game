using System;

/// <summary>显隐作用对象。</summary>
public enum TimelineVisibilityTargetType
{
    /// <summary>施法者。</summary>
    Caster = 0,
    /// <summary>施法对象。</summary>
    CastTargets = 1,
    /// <summary>整个战斗（场景建筑根 + 单位；可用 <see cref="TimelineVisibilityBattleExcludeType"/> 排除）。</summary>
    Battle = 2,
    /// <summary>效果来源。</summary>
    EffectSource = 3,
    /// <summary>效果目标。</summary>
    EffectTargets = 4,
    /// <summary>与任一效果目标同队的其他单位，排除所有效果目标。</summary>
    EffectTargetTeammates = 5,
    /// <summary>与施法者同队的其他单位。</summary>
    CasterTeammates = 6,
    /// <summary>与任一施法对象同队的其他单位，排除所有施法对象。</summary>
    CastTargetTeammates = 7,
    /// <summary>与效果来源同队的其他单位。</summary>
    EffectSourceTeammates = 8,
    /// <summary>施法者之外的所有友方和敌方单位，不包含场景建筑。</summary>
    OtherUnits = 9,
}

/// <summary>
/// 「整个战斗」时的排除类型（可多选）。
/// </summary>
[Flags]
public enum TimelineVisibilityBattleExcludeType
{
    None = 0,
    /// <summary>施法者。</summary>
    Caster = 1 << 0,
    /// <summary>施法者的其他队友。</summary>
    CasterTeammates = 1 << 1,
    /// <summary>所有敌方。</summary>
    AllEnemies = 1 << 2,
    /// <summary>施法对象。</summary>
    CastTargets = 1 << 3,
    /// <summary>效果来源。</summary>
    EffectSource = 1 << 4,
    /// <summary>效果目标。</summary>
    EffectTargets = 1 << 5,
}

/// <summary>
/// 统一显隐：按目标类型切换单位根节点或战场建筑渲染。
/// 勾选 <see cref="ResetOnEnd"/> 时 Clip 结束还原；默认不勾，结束后保持显隐状态。
/// </summary>
[Serializable]
public class TimelineClipVisibilityConfig : TimelineClipConfigBase
{
    /// <summary>整个战斗默认排除施法者和施法对象。</summary>
    public const TimelineVisibilityBattleExcludeType DefaultBattleExcludeTypes =
        TimelineVisibilityBattleExcludeType.Caster | TimelineVisibilityBattleExcludeType.CastTargets;

    /// <summary>是否显示。</summary>
    public bool Show = true;

    /// <summary>作用对象。</summary>
    public TimelineVisibilityTargetType TargetType = TimelineVisibilityTargetType.Caster;

    /// <summary>效果绑定的 ClipKey。留空时取显隐开始时最近的效果绑定。</summary>
    public string EffectBindingKey;

    /// <summary>
    /// 仅 <see cref="TimelineVisibilityTargetType.Battle"/> 生效：从「整个战斗」中排除的单位类型（可多选）。
    /// 默认勾选施法者与施法对象。
    /// </summary>
    public TimelineVisibilityBattleExcludeType BattleExcludeTypes = DefaultBattleExcludeTypes;

    /// <summary>
    /// 勾选后 Clip 结束时还原进条前的显隐；默认不勾，结束后保持本次设置。
    /// </summary>
    public bool ResetOnEnd;
}
