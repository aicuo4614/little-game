using System;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client
{
    public sealed partial class BattleHud
    {
        public void OpenPanels()
        {
            statusPanel?.InternalOpen();
            actionsPanel?.InternalOpen();
        }
        public void ClosePanels()
        {
            statusPanel?.InternalClose();
            actionsPanel?.InternalClose();
        }
        private sealed class ActionsPanel : ModulePanelBase
        {
            private readonly BattleHud hud;
            private string signature;
            private long lastCheckedSeq = -1;
            private int lastCheckedSelected = -1, lastCheckedHidden = -1;
            public ActionsPanel(BattleHud hud) { this.hud = hud; }
            public void Invalidate() { signature = null; }
            protected override void OnOpen() { Invalidate(); }
            protected override void OnClose()
            {
                hud.targets.Clear();
                hud.ClearRecruitFlyAnims(revealAll: true);
                hud.CancelUi();
            }
            public void Tick(BattleSession s)
            {
                if (!IsOpened) return;
                if (signature == null || s.LastSequence != lastCheckedSeq || hud.Selected != lastCheckedSelected || hud.hiddenReserveIds.Count != lastCheckedHidden)
                {
                    lastCheckedSeq = s.LastSequence;
                    lastCheckedSelected = hud.Selected;
                    lastCheckedHidden = hud.hiddenReserveIds.Count;
                    string hiddenSig = hud.hiddenReserveIds.Count > 0 ? string.Join(",", hud.hiddenReserveIds.OrderBy(id => id)) : "";
                    string next = s.Phase + "/" + s.Spins + "/" + hud.Selected + "/" + s.Aim + "/" + s.Paused + "/" + s.Speed + "/" + s.Tactics
                    + "/" + s.MaxDeployed + "/" + s.PopExp + "/" + hud.confirmStart + hud.confirmRestart + hud.confirmRecycle
                    + "/" + hiddenSig
                    + "/" + string.Join(",", s.Troops.Select(t => $"{t.Id}:{t.Star}:{t.Lane}:{t.Row}"))
                    + "/" + string.Join(",", s.Rewards) + "/" + string.Join("", s.Reels.Select(r => (int)r));
                    if (next != signature) { signature = next; hud.Rebuild(); }
                }

            }
        }
    }
}
