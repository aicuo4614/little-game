namespace LittleGame.Client.Battle
{
    /// <summary>战斗 UGUI 窗口管理子系统（对齐 Demo BattleHudControl）。</summary>
    public sealed class BattleHudControl : BattleControlBase
    {
        public BattleWindow Window { get; private set; }
        public BattleHud Hud => Window?.Hud;

        public void BindWindow(BattleWindow window)
        {
            Window = window;
            Window?.BindBattle(Owner.App, Owner);
        }

        public void Refresh()
        {
            Window?.Refresh();
        }
    }
}
