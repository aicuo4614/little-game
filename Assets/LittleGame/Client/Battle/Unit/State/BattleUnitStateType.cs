using System;

namespace LittleGame.Client.Battle
{
    /// <summary>单位表现行为状态枚举（对齐 Demo BattleUnitStateType，扩展受控态）。</summary>
    public enum BattleUnitStateType
    {
        None = 0,
        Idle = 1,
        Move = 2,
        Skill = 3,
        Controlled = 4,
        Die = 5,
    }

    /// <summary>单位高亮标记类型（对齐 Demo BattleUnitMarkType）。</summary>
    public enum BattleUnitMarkType
    {
        None = 0,
        Selected = 1,
        Focused = 2,
        Protected = 3,
    }
}
