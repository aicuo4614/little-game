using LittleGame.Combat;

namespace LittleGame.Client.Battle
{
    /// <summary>战斗规则事件处理器基类（对齐 Demo ActionHandlerBase）。</summary>
    public abstract class ActionHandlerBase
    {
        protected BattleActionControl Control { get; private set; }
        protected BattleView View => Control.Owner;

        public void Init(BattleActionControl control)
        {
            Control = control;
        }

        public abstract bool CanHandle(CombatEvent ev);
        public abstract void Handle(BattleSession session, CombatEvent ev);
    }
}
