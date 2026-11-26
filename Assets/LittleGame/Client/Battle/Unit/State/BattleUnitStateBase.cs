namespace LittleGame.Client.Battle
{
    /// <summary>单位行为状态基类（对齐 Demo BattleUnitStateBase），负责进入、逐帧更新、退出与表现恢复。</summary>
    public abstract class BattleUnitStateBase
    {
        protected BattleUnit unit;
        protected BattleUnitStateControl control;

        public void Init(BattleUnit owner, BattleUnitStateControl stateControl)
        {
            unit = owner;
            control = stateControl;
        }

        public virtual void OnEnter() { }
        public virtual void OnUpdate(float deltaTime) { }
        public virtual void OnExit() { }
        public virtual void RefreshPresentation() { }
    }
}
