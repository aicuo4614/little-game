using PureMVC.Interfaces;
using PureMVC.Patterns.Mediator;

namespace LittleGame.Client
{
    // 从 Demo 采用的 PureMVC 分离界面刷新与玩法指令。
    public sealed class BattleMediator : Mediator
    {
        public const string MediatorKey = "CombatView";
        private readonly IBattleView view;
        public BattleMediator(IBattleView view) : base(MediatorKey, view) { this.view = view; }
        public override string[] ListNotificationInterests() => new[] { BattleApplication.Changed };
        public override void HandleNotification(INotification notification) => view.Refresh();
    }
}
