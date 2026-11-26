namespace LittleGame.Client.Battle
{
    /// <summary>待机状态：循环播放 3D Animator 的 Idle 状态。</summary>
    public sealed class BattleUnitIdleState : BattleUnitStateBase
    {
        public override void OnEnter()
        {
            RefreshPresentation();
        }

        public override void RefreshPresentation()
        {
            unit.AnimatorControl.PlayIdle();
        }
    }
}
