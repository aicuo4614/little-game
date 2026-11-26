using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 战斗业务模块入口（严格对齐 Demo ModuleBase&lt;TArgs&gt; + ModuleMgr）：
    /// 由 ModuleMgr.Instance.OpenModule("Battle") 反射创建并驱动，
    /// 组装 BattleApplication、BattleView（9个子系统）、UGUI BattleWindow（ModuleWindowBase）与 BattleStateMachine。
    /// </summary>
    public sealed class BattleModule : ModuleBase<ModuleNoArgs>
    {
        public override string ModuleName => "Battle";
        public override bool NeedShowLoadingOnOpen => false;

        public BattleApplication App { get; private set; }
        public BattleView View { get; private set; }
        public BattleWindow Window { get; private set; }
        public BattleHud Hud => Window?.Hud;
        public BattleInput Input => View?.InputControl?.Input;
        public BattleStateMachine StateMachine { get; private set; }

        private GameObject viewHost;

        protected override Task OnOpenAsync(ModuleNoArgs args)
        {
            if (App == null)
            {
                var store = new LocalRunStore(Path.Combine(Application.persistentDataPath, "combat-run-v1.json"));
                App = new BattleApplication(store);
                App.PersistenceError += message => GameDebug.LogWarning("CombatSave", message);
            }

            if (View == null)
            {
                viewHost = new GameObject("BattleViewHost");
                View = viewHost.AddComponent<BattleView>();
                View.Build(App);
            }

            Window = OpenDynamicWindow<BattleWindow>("BattleWindow");
            View.HudControl.BindWindow(Window);
            View.InputControl.BindHud(Window.Hud);
            App.AttachView(Window.Hud);

            StateMachine = new BattleStateMachine();
            StateMachine.Init(this);
            StateMachine.SyncPhase(App.Session.Phase);

            MVC.SendNotification(BattleNoti.BATTLE_START, this);
            return Task.CompletedTask;
        }

        public void Tick(float deltaTime)
        {
            if (App == null) return;
            App.Update(deltaTime);
            StateMachine?.SyncPhase(App.Session.Phase);
            StateMachine?.Tick(deltaTime);
        }

        public override void Destroy()
        {
            StateMachine?.Clear();
            StateMachine = null;

            if (View != null)
            {
                View.Teardown();
                View = null;
            }
            if (viewHost != null)
            {
                UnityEngine.Object.Destroy(viewHost);
                viewHost = null;
            }

            App?.Dispose();
            App = null;
            Window = null;

            MVC.SendNotification(BattleNoti.BATTLE_END);
            base.Destroy();
        }
    }
}
