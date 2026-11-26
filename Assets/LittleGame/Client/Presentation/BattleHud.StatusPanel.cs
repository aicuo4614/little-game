using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Client.Battle;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace LittleGame.Client
{
    public sealed partial class BattleHud
    {
        /// <summary>状态/人口/敌情面板，独立持有组件和刷新缓存。</summary>
        private sealed class StatusPanel : ModulePanelBase
        {
            private readonly BattleHud hud;
            private Text status, forecast, instruction;
            private Text squadText, phaseLabel, reserveTitle, popText;
            private Image popRingFill;
            private readonly List<Image> wavePips = new List<Image>();
            private int lastHudWave = -1, lastHudAlive = -1, lastHudDeployed = -1, lastHudMaxDeployed = -1, lastHudPopExp = -1, lastHudTactics = -1, lastHudReserve = -1;
            private float lastHudMarch = -1f;
            private RunPhase lastHudPhase = (RunPhase)(-1);
            private AimKind lastHudAim = (AimKind)(-1);
            private bool lastHudPaused, lastHudEnraged, lastHudDropHighlight;

            public StatusPanel(BattleHud hud) { this.hud = hud; }
            protected override void OnInit()
            {
                hud.Icon(Root, "Shield", new Rect(.035f, .948f, .072f, .044f));
                hud.Text(Root, "符阵军团", new Rect(.12f, .946f, .37f, .05f), 26, TextAnchor.MiddleLeft, CombatUiSkin.Paper).fontStyle = FontStyle.Bold;
                hud.Text(Root, "无尽遗迹  ·  远征", new Rect(.12f, .930f, .37f, .025f), 12, TextAnchor.MiddleLeft, new Color(.7f, .67f, .78f));

                var popRoot = hud.Panel(Root, "PopRing", new Rect(.505f, .936f, .295f, .056f), Color.clear);
                hud.CreateRingImage(popRoot, "PopRingBg", new Rect(0f, .04f, .27f, .92f), new Color(.36f, .34f, .48f, .95f), 1f);
                popRingFill = hud.CreateRingImage(popRoot, "PopRingFill", new Rect(0f, .04f, .27f, .92f), CombatUiSkin.Gold, 0f);
                popText = hud.Text(popRoot, "人口 0/6", new Rect(.29f, .04f, .71f, .92f), 15, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
                popText.fontStyle = FontStyle.Bold;

                status = hud.Text(Root, "", new Rect(.035f, .892f, .42f, .038f), 20, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
                squadText = hud.Text(Root, "", new Rect(.505f, .894f, .45f, .034f), 14, TextAnchor.MiddleLeft, new Color(.84f, .92f, .96f));
                for (int i = 0; i < 12; i++) wavePips.Add(hud.Panel(Root, "Wave" + i, new Rect(.04f + i * .026f, .884f, .018f, .004f), CombatUiSkin.Gold).GetComponent<Image>());

                var forecastCard = hud.Panel(Root, "ForecastBanner", new Rect(.03f, .852f, .94f, .028f), Color.white);
                CombatUiSkin.Decorate(forecastCard, "ButtonDark");
                forecast = hud.Text(forecastCard, "", new Rect(.03f, .02f, .94f, .96f), 12, TextAnchor.MiddleCenter, CombatUiSkin.Gold);

                hud.Panel(Root, "InstructionBackground", new Rect(0, .81f, 1, .04f), new Color(.105f, .09f, .16f));
                var rule = hud.Panel(Root, "RuleBanner", new Rect(.06f, .817f, .88f, .031f), new Color(.12f, .12f, .18f, .9f));
                instruction = hud.Text(rule, "", new Rect(.02f, 0, .96f, 1), 12, TextAnchor.MiddleCenter, CombatUiSkin.Paper);

                phaseLabel = hud.Text(Root, "", new Rect(.06f, .296f, .44f, .026f), 14, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
                reserveTitle = hud.Text(Root, "", new Rect(.51f, .296f, .43f, .026f), 14, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
            }
            public void Tick(BattleSession s)
            {
                if (!IsOpened) return;
                // 仅在数据变化时更新顶部/底栏文本，消除每帧字符串分配与 Canvas 网格重建
                if (s.Wave != lastHudWave)
                {
                    lastHudWave = s.Wave;
                    status.text = $"第 {s.Wave:00} 波 <size=12>/ {BattleDefinitions.TotalWaves}</size>";
                    for (int i = 0; i < wavePips.Count; i++)
                    wavePips[i].color = i < s.Wave ? CombatUiSkin.Gold : new Color(.28f, .26f, .35f);
                    forecast.text = BattleDefinitions.IsBoss(s.Wave)
                    ? "首领来袭  ·  石甲巨像  ·  全军自由合围迎战"
                    : "前方敌情  ·  " + string.Join("  /  ", s.Forecast.GroupBy(e => e.Kind).Select(g => BattleDefinitions.Name(g.Key) + "×" + g.Count()));
                }

                int aliveCount = s.AliveTroopCount;
                int deployedCount = s.DeployedCount;
                if (s.Phase != lastHudPhase || aliveCount != lastHudAlive || deployedCount != lastHudDeployed || s.Enraged != lastHudEnraged || Math.Abs(s.MarchDistance - lastHudMarch) > 0.1f)
                {
                    lastHudAlive = aliveCount;
                    lastHudEnraged = s.Enraged;
                    lastHudMarch = s.MarchDistance;
                    if (squadText != null)
                    {
                        squadText.text = s.Phase == RunPhase.Fighting
                        ? $"存活军团  {aliveCount} / {deployedCount}" + (s.Enraged ? "  · 敌军狂暴" : "")
                        : $"远征推进  {Mathf.RoundToInt(s.MarchDistance)}m  ·  波后全员满血";
                        squadText.color = s.Enraged ? new Color(1f, .46f, .42f) : new Color(.84f, .92f, .96f);
                    }
                }

                if (deployedCount != lastHudDeployed || s.MaxDeployed != lastHudMaxDeployed || s.PopExp != lastHudPopExp)
                {
                    lastHudDeployed = deployedCount;
                    lastHudMaxDeployed = s.MaxDeployed;
                    lastHudPopExp = s.PopExp;
                    if (popRingFill != null)
                    {
                        float ratio = s.PopExpNeeded <= 0 ? 1f : Mathf.Clamp01((float)s.PopExp / s.PopExpNeeded);
                        popRingFill.fillAmount = ratio;
                        popRingFill.color = deployedCount >= s.MaxDeployed ? new Color(1f, 0.72f, 0.30f) : new Color(0.45f, 0.92f, 0.78f);
                    }
                    if (popText != null)
                    popText.text = $"人口 {deployedCount}/{s.MaxDeployed}";
                }

                int visibleReserveCount = 0;
                var allTroops = s.Troops;
                for (int i = 0; i < allTroops.Count; i++)
                if (!allTroops[i].Deployed && !hud.hiddenReserveIds.Contains(allTroops[i].Id)) visibleReserveCount++;

                if (s.Phase != lastHudPhase || s.Tactics != lastHudTactics || s.Aim != lastHudAim || s.Paused != lastHudPaused || visibleReserveCount != lastHudReserve || hud.reserveDropHighlighted != lastHudDropHighlight)
                {
                    lastHudPhase = s.Phase;
                    lastHudTactics = s.Tactics;
                    lastHudAim = s.Aim;
                    lastHudPaused = s.Paused;
                    lastHudReserve = visibleReserveCount;
                    lastHudDropHighlight = hud.reserveDropHighlighted;
                    phaseLabel.text = s.Phase == RunPhase.Preparation ? "十字法阵" : s.Phase == RunPhase.Fighting ? "战术指挥  ·  本波剩余 " + s.Tactics + " 次" : s.Phase == RunPhase.Reward ? "远征奖励  ·  选择一项强化" : "远征战报";
                    reserveTitle.gameObject.SetActive(s.Phase == RunPhase.Preparation);
                    reserveTitle.text = hud.reserveDropHighlighted ? "松手召回至候补军团" : "候补军团  " + visibleReserveCount + "/6";
                    reserveTitle.color = hud.reserveDropHighlighted ? new Color(0.48f, 1f, 0.80f) : CombatUiSkin.Gold;
                    instruction.text = s.Aim != AimKind.None
                    ? "瞄准已暂停 · 点目标确认，或取消"
                    : s.Phase == RunPhase.Preparation
                    ? (deployedCount == 0
                    ? "点击招募获得军团 · 拖动或双击候补上阵"
                    : "拖动或双击候补上阵 · 拖回候补区召回 · 三同自动升星")
                    : s.Phase == RunPhase.Fighting
                    ? (s.Paused ? "已暂停 · 继续后战斗推进" : "自由转向交锋 · 每波一次集火或保护")
                    : s.Phase == RunPhase.Reward ? "选择一项本局强化"
                    : s.Phase == RunPhase.Won ? "十二波远征大捷！"
                    : s.Phase == RunPhase.Lost ? "全军覆没 · 调整阵容再战"
                    : "本波告捷！全员满血推进，人口经验 +2";
                }

            }
        }
    }
}
