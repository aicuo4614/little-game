using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 战斗主窗口（继承 HotUpdate_Main 的 UGUI ModuleWindowBase，分层挂接在 UiSortingLayer.Module）：
    /// 封装现有高完整度 UGUI BattleHud 并以标准 ModulePanelBase 生命周期管理子面板。
    /// </summary>
    public sealed class BattleWindow : ModuleWindowBase
    {
        protected override UiSortingLayer SortingLayer => UiSortingLayer.Module;

        public BattleHud Hud { get; private set; }
        public void BindBattle(BattleApplication app, BattleView view)
        {
            if (Hud == null)
            {
                var hostGo = new GameObject("BattleHudHost", typeof(RectTransform));
                hostGo.transform.SetParent(Root != null ? Root : transform, false);
                Hud = hostGo.AddComponent<BattleHud>();
                Hud.Build(app, view, this);
            }

        }

        protected override void OnOpen() { Hud?.OpenPanels(); }
        protected override void OnHide() { Hud?.ClosePanels(); }


        public void Refresh()
        {
            Hud?.Refresh();
        }

        protected override void OnClose()
        {
            Hud?.ClosePanels();
            base.OnClose();
        }
    }

}
