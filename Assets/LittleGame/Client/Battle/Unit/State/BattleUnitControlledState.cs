namespace LittleGame.Client.Battle
{
    /// <summary>受控状态（冻结/眩晕）：中断当前技能并暂停 3D Animator 播放，退出时恢复。</summary>
    public sealed class BattleUnitControlledState : BattleUnitStateBase
    {
        public override void OnEnter()
        {
            unit.AnimatorControl.SetFrozen(true);
        }

        public override void RefreshPresentation()
        {
            unit.AnimatorControl.SetFrozen(true);
        }

        public override void OnExit()
        {
            unit.AnimatorControl.SetFrozen(false);
        }
    }
}
