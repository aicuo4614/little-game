using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace LittleGame.Client
{
    public sealed partial class BattleHud
    {
        /// <summary>关卡进度、人口、羁绊与伤害统计，读取规则层数据。</summary>
        private sealed class StatusPanel : ModulePanelBase
        {
            private readonly BattleHud hud;
            private readonly List<Image> waveNodes = new List<Image>();
            private readonly List<Image> waveOutlines = new List<Image>();
            private readonly List<Image> populationSlots = new List<Image>();
            private readonly List<HudTarget> panelTargets = new List<HudTarget>();
            private readonly List<RectTransform> synergyTiles = new List<RectTransform>();
            private readonly List<Image> synergyIcons = new List<Image>();
            private readonly List<Text> synergyCounts = new List<Text>();
            private readonly List<Image[]> synergyDots = new List<Image[]>();
            private readonly List<IGrouping<(UnitRace?, UnitJob?), SynergyDefinition>> synergyGroups = new List<IGrouping<(UnitRace?, UnitJob?), SynergyDefinition>>();
            private readonly List<RectTransform> damageRows = new List<RectTransform>();
            private readonly List<Image> portraits = new List<Image>();
            private readonly List<Text> stars = new List<Text>();
            private readonly List<Text> damageValues = new List<Text>();
            private readonly List<RectTransform> bars = new List<RectTransform>();
            private Sprite circle;
            public void DisposeVisuals()
            {
                if (circle != null) { UnityEngine.Object.Destroy(circle.texture); UnityEngine.Object.Destroy(circle); }
            }
            private Image progress;
            private Text popText, instruction, phaseLabel, reserveTitle, waveTab, runTab, emptyDamage, damageTitle;
            private RectTransform instructionRoot, damageRoot;
            private bool total;
            private float nextRefresh;
            private BattleSession lastSession;
            private RunPhase lastPhase = (RunPhase)(-1);

            public StatusPanel(BattleHud hud) { this.hud = hud; }
            private const float DamageScale = .70710678f;
            private const float DamageWidth = .44f * DamageScale;
            private const float DamageRight = .96f;
            private static readonly Color Dim = new Color(.34f, .29f, .39f);

            protected override void OnInit()
            {
                hud.Text(Root, "遗迹庭院", new Rect(.18f, .945f, .64f, .04f), 25, TextAnchor.MiddleCenter, CombatUiSkin.Gold);
                hud.Panel(Root, "WaveTrack", new Rect(.07f, .912f, .86f, .004f), Dim);
                progress = hud.Panel(Root, "WaveProgress", new Rect(.07f, .912f, .86f, .004f), CombatUiSkin.Gold).GetComponent<Image>();
                progress.type = Image.Type.Filled;
                progress.fillMethod = Image.FillMethod.Horizontal;
                var circleTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        circleTexture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(31.5f - Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)))));
                circleTexture.Apply();
                circle = Sprite.Create(circleTexture, new Rect(0, 0, 64, 64), Vector2.one * .5f);
                progress.sprite = circle;
                for (int i = 0; i < BattleDefinitions.TotalWaves; i++)
                {
                    bool boss = BattleDefinitions.IsBoss(i + 1);
                    float w = boss ? .062f : .035f;
                    var node = hud.Panel(Root, "WaveNode_" + (i + 1), new Rect(.07f + i * .86f / Math.Max(1, BattleDefinitions.TotalWaves - 1) - w / 2, .914f - w * .28125f, w, w * .5625f), Color.white);
                    var image = node.GetComponent<Image>(); image.sprite = circle;
                    waveNodes.Add(image);
                    waveOutlines.Add(hud.CreateRingImage(node, "CurrentWave", new Rect(-.12f, -.12f, 1.24f, 1.24f), CombatUiSkin.Gold, 1));
                    if (boss) hud.Icon(node, "Boss", new Rect(.18f, .18f, .64f, .64f));
                }
                hud.Icon(Root, "Population", new Rect(.64f, .864f, .045f, .026f));
                for (int i = 0; i < 9; i++) populationSlots.Add(hud.Panel(Root, "Population_" + i, new Rect(.70f + i * .013f, .869f, .007f, .014f), Dim).GetComponent<Image>());
                popText = hud.Text(Root, "", new Rect(.83f, .861f, .13f, .032f), 14, TextAnchor.MiddleRight, CombatUiSkin.Gold);
                instructionRoot = hud.Panel(Root, "PreparationHint", new Rect(.04f, .335f, .92f, .031f), CombatUiSkin.Ink);
                instruction = hud.Text(instructionRoot, "", new Rect(.02f, 0, .96f, 1), 11, TextAnchor.MiddleCenter, CombatUiSkin.Paper);
                phaseLabel = hud.Text(Root, "", new Rect(.06f, .296f, .44f, .026f), 14, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
                reserveTitle = hud.Text(Root, "", new Rect(.51f, .296f, .43f, .026f), 14, TextAnchor.MiddleLeft, CombatUiSkin.Gold);

                synergyGroups.AddRange(BattleTables.Current.Synergies.GroupBy(s => (s.Race, s.Job)));
                int capacity = Math.Max(8, synergyGroups.Count);
                for (int i = 0; i < capacity; i++)
                {
                    var tile = hud.Panel(Root, "Synergy_" + i, new Rect(0, 0, .085f, .0478125f), Color.white);
                    CombatUiSkin.Decorate(tile, "CardBack", new Color(.20f, .17f, .26f));
                    synergyTiles.Add(tile);
                    synergyIcons.Add(hud.Icon(tile, "Shield", new Rect(.25f, .51f, .5f, .43f)));
                    synergyCounts.Add(hud.Text(tile, "", new Rect(.05f, .15f, .9f, .38f), 11, TextAnchor.MiddleCenter, CombatUiSkin.Paper));
                    var dots = new Image[2];
                    for (int d = 0; d < 2; d++)
                    {
                        dots[d] = hud.Panel(tile, "Tier_" + d, new Rect(.35f + d * .2f, .06f, .11f, .11f), Dim).GetComponent<Image>();
                        dots[d].sprite = circle;
                    }
                    synergyDots.Add(dots);
                    tile.gameObject.SetActive(false);
                }
                damageRoot = hud.Panel(Root, "DamageStatistics", new Rect(DamageRight - DamageWidth, HeaderBottom - .215f * DamageScale, DamageWidth, .215f * DamageScale), Color.white);
                CombatUiSkin.Decorate(damageRoot, "Panel", Color.white, true, 5);
                damageTitle = hud.Text(damageRoot, "伤害统计", new Rect(.06f, .83f, .45f, .12f), 11, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
                waveTab = hud.Text(damageRoot, "本波", new Rect(.53f, .83f, .20f, .12f), 10);
                runTab = hud.Text(damageRoot, "本局", new Rect(.75f, .83f, .20f, .12f), 10);
                panelTargets.Add(new HudTarget { Id = "damage_wave", Rect = hud.Panel(damageRoot, "WaveTabHit", new Rect(.53f, 0, .20f, .1f), Color.clear), UiCamera = hud.uiCamera, Click = () => { total = false; nextRefresh = 0; } });
                panelTargets.Add(new HudTarget { Id = "damage_run", Rect = hud.Panel(damageRoot, "RunTabHit", new Rect(.75f, 0, .20f, .1f), Color.clear), UiCamera = hud.uiCamera, Click = () => { total = true; nextRefresh = 0; } });
                emptyDamage = hud.Text(damageRoot, "开战后记录伤害", new Rect(.08f, .27f, .84f, .35f), 10, TextAnchor.MiddleCenter, CombatUiSkin.Paper);
                for (int i = 0; i < 8; i++)
                {
                    var row = hud.Panel(damageRoot, "DamageRow_" + i, new Rect(.05f, .05f, .9f, .16f), Color.clear);
                    damageRows.Add(row);
                    portraits.Add(hud.Icon(row, "Helmet", new Rect(0, 0, .23f, 1)));
                    stars.Add(hud.Text(row, "", new Rect(.27f, .32f, .4f, .68f), 9, TextAnchor.MiddleLeft, CombatUiSkin.Gold));
                    damageValues.Add(hud.Text(row, "", new Rect(.64f, .32f, .36f, .68f), 10, TextAnchor.MiddleRight));
                    hud.Panel(row, "DamageTrack", new Rect(.27f, .16f, .73f, .23f), Dim);
                    bars.Add(hud.Panel(row, "DamageBar", new Rect(.27f, .16f, .73f, .23f), new Color(.97f, .62f, .25f)));
                }
            }

            public object Snapshot() => new
            {
                damageScope = total ? "run" : "wave",
                layout = new
                {
                    headerBottom = HeaderBottom,
                    damageTop = damageRoot.anchorMax.y,
                    damageWidth = DamageWidth,
                    damageAreaRatio = DamageScale * DamageScale,
                    synergyTop = synergyTiles.Where(tile => tile.gameObject.activeSelf).Select(tile => tile.anchorMax.y).DefaultIfEmpty(HeaderBottom).Max()
                },
                damageRows = damageRows.Select((row, i) => new { visible = row.gameObject.activeSelf, hero = portraits[i].sprite != null ? portraits[i].sprite.name : null, value = damageValues[i].text }),
                synergies = synergyTiles.Select((tile, i) => new { visible = tile.gameObject.activeSelf, count = synergyCounts[i].text }),
                progress = progress.fillAmount,
                population = popText.text
            };

            public void RegisterTargets() { hud.targets.AddRange(panelTargets); }

            public void Tick(BattleSession session)
            {
                if (!IsOpened) return;
                if (session != lastSession) { total = false; nextRefresh = 0; lastSession = session; }
                if (session.Phase != lastPhase) { nextRefresh = 0; lastPhase = session.Phase; }
                bool preparation = session.Phase == RunPhase.Preparation;
                instructionRoot.gameObject.SetActive(preparation);
                var selected = session.Troops.FirstOrDefault(t => t.Id == hud.Selected);
                if (preparation && Time.unscaledTime >= nextRefresh)
                    instruction.text = selected != null ? selected.Definition.Name + " · " + string.Join(" / ", selected.Definition.Skills.Take(selected.Star).Where(s => s != null).Select(s => s.Name)) : "拖动或双击候补上阵 · 同名同星自动合成";
                phaseLabel.text = preparation ? "十字法阵" : session.Phase == RunPhase.Reward ? "远征奖励" : "";
                reserveTitle.gameObject.SetActive(preparation);
                if (preparation && Time.unscaledTime >= nextRefresh)
                    reserveTitle.text = hud.reserveDropHighlighted ? "松手召回至候补" : "候补军团 " + session.Troops.Count(t => !t.Deployed && !hud.hiddenReserveIds.Contains(t.Id)) + "/6";
                int cleared = session.Wave - 1;
                if (session.Phase == RunPhase.WaveResult || session.Phase == RunPhase.Reward || session.Phase == RunPhase.Won) cleared = session.Wave;
                progress.fillAmount = Mathf.Max(0, cleared - 1f) / Math.Max(1, waveNodes.Count - 1);
                for (int i = 0; i < waveNodes.Count; i++)
                {
                    var nodeRect = waveNodes[i].rectTransform;
                    float halfHeight = (nodeRect.anchorMax.x - nodeRect.anchorMin.x) * Root.rect.width / Mathf.Max(1, Root.rect.height) * .5f;
                    nodeRect.anchorMin = new Vector2(nodeRect.anchorMin.x, .914f - halfHeight);
                    nodeRect.anchorMax = new Vector2(nodeRect.anchorMax.x, .914f + halfHeight);
                    waveNodes[i].color = i < cleared ? CombatUiSkin.Gold : Dim;
                    waveOutlines[i].gameObject.SetActive(i == session.Wave - 1 && i >= cleared);
                }
                if (Time.unscaledTime >= nextRefresh) popText.text = session.DeployedCount + "/" + session.MaxDeployed;
                for (int i = 0; i < populationSlots.Count; i++)
                {
                    populationSlots[i].gameObject.SetActive(i < session.MaxDeployed);
                    populationSlots[i].color = i < session.DeployedCount ? CombatUiSkin.Gold : Dim;
                }
                if (Time.unscaledTime < nextRefresh) return;
                nextRefresh = Time.unscaledTime + .5f;
                RefreshSynergies(session);
                RefreshDamage(session);
            }

            private void RefreshSynergies(BattleSession session)
            {
                var heroes = session.Troops.Where(t => t.Deployed).GroupBy(t => t.Identity).Select(g => g.First().Definition).ToArray();
                int visible = 0;
                foreach (var group in synergyGroups)
                {
                    int count = heroes.Count(u => group.First().Matches(u));
                    if (count == 0) continue;
                    var thresholds = group.Select(s => s.Count).Distinct().OrderBy(n => n).ToArray();
                    int next = thresholds.FirstOrDefault(n => n > count);
                    if (next == 0) next = thresholds.Last();
                    var tile = synergyTiles[visible];
                    tile.gameObject.SetActive(true);
                    float tileHeight = .085f * Root.rect.width / Mathf.Max(1, Root.rect.height);
                    float top = HeaderBottom - visible / 4 * (tileHeight + .007f);
                    tile.anchorMin = new Vector2(.04f + visible % 4 * .1f, top - tileHeight);
                    tile.anchorMax = tile.anchorMin + new Vector2(.085f, tileHeight);
                    synergyCounts[visible].text = count + "/" + next;
                    string key = group.Key.Item1.HasValue ? (group.Key.Item1 == UnitRace.Human ? "Human" : "Undead") : CombatUiSkin.TroopIcon((int)group.Key.Item2.Value);
                    synergyIcons[visible].sprite = CombatUiSkin.Sprite(key);
                    synergyIcons[visible].color = count >= thresholds[0] ? Color.white : new Color(.55f, .55f, .6f);
                    for (int d = 0; d < 2; d++)
                    {
                        synergyDots[visible][d].gameObject.SetActive(d < thresholds.Length);
                        synergyDots[visible][d].color = d < thresholds.Length && count >= thresholds[d] ? CombatUiSkin.Gold : Dim;
                    }
                    visible++;
                }
                for (int i = visible; i < synergyTiles.Count; i++) synergyTiles[i].gameObject.SetActive(false);
            }

            private void RefreshDamage(BattleSession session)
            {
                waveTab.color = total ? CombatUiSkin.Paper : CombatUiSkin.Gold;
                runTab.color = total ? CombatUiSkin.Gold : CombatUiSkin.Paper;
                var rows = session.DamageStatistics.Where(r => total || r.WaveDamage > 0).OrderByDescending(r => total ? r.TotalDamage : r.WaveDamage).ThenBy(r => r.HeroId).Take(8).ToArray();
                emptyDamage.gameObject.SetActive(rows.Length == 0);
                float height = (.058f + Math.Max(1, rows.Length) * .039f) * DamageScale;
                damageRoot.anchorMin = new Vector2(DamageRight - DamageWidth, HeaderBottom - height);
                damageRoot.anchorMax = new Vector2(.96f, HeaderBottom);
                foreach (var tab in panelTargets)
                {
                    tab.Rect.anchorMin = new Vector2(tab.Rect.anchorMin.x, 1f - .047f / height);
                    tab.Rect.anchorMax = new Vector2(tab.Rect.anchorMax.x, 1f);
                }
                for (int tabIndex = 0; tabIndex < 2; tabIndex++)
                {
                    var label = tabIndex == 0 ? waveTab : runTab;
                    label.rectTransform.anchorMin = new Vector2(label.rectTransform.anchorMin.x, 1f - .034f * DamageScale / height);
                    label.rectTransform.anchorMax = new Vector2(label.rectTransform.anchorMax.x, 1f - .009f * DamageScale / height);
                }
                var title = damageTitle;
                title.rectTransform.anchorMin = new Vector2(.06f, 1f - .034f * DamageScale / height);
                title.rectTransform.anchorMax = new Vector2(.5f, 1f - .009f * DamageScale / height);
                float maximum = rows.Length == 0 ? 1 : Math.Max(1, total ? rows[0].TotalDamage : rows[0].WaveDamage);
                for (int i = 0; i < damageRows.Count; i++)
                {
                    damageRows[i].gameObject.SetActive(i < rows.Length);
                    if (i >= rows.Length) continue;
                    float rowHeight = .033f * DamageScale / height;
                    damageRows[i].anchorMin = new Vector2(.05f, 1f - (.052f + i * .039f) * DamageScale / height - rowHeight);
                    damageRows[i].anchorMax = damageRows[i].anchorMin + new Vector2(.90f, rowHeight);
                    var record = rows[i];
                    portraits[i].sprite = CombatUiSkin.Portrait(record.HeroId);
                    stars[i].text = new string('★', Math.Max(record.Star, session.Troops.Where(t => t.Identity == record.HeroId).Select(t => t.Star).DefaultIfEmpty(1).Max()));
                    float amount = total ? record.TotalDamage : record.WaveDamage;
                    damageValues[i].text = Mathf.FloorToInt(amount).ToString("N0");
                    bars[i].anchorMax = new Vector2(.27f + .73f * amount / maximum, .39f);
                }
            }
        }
    }
}
