/// <summary>表现片段所操作的单位角色；保留旧位移枚举的前三个数值。</summary>
public enum TimelineUnitSubjectType
{
    /// <summary>本次施法者。</summary>
    Caster = 0,
    /// <summary>所绑定效果的来源。</summary>
    EffectSource = 1,
    /// <summary>所绑定效果的全部目标。</summary>
    EffectTargets = 2,
    /// <summary>所绑定施法的全部对象。</summary>
    CastTargets = 3,
}
