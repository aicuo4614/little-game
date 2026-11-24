using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace LittleGame.Client
{
    public sealed class HudTarget
    {
        public string Id; public RectTransform Rect; public Action Click; public int UnitId;
        public Vector2 Center => RectTransformUtility.WorldToScreenPoint(null, Rect.position);
    }
    /// <summary>只将交互翻译为正常指令；动态面板由状态签名驱动刷新。</summary>
    public sealed class BattleHud : MonoBehaviour, IBattleView
    {
        private BattleApplication app;
        private BattleSceneView view;
        private RectTransform root, dynamic;
        private Text status, forecast, message, instruction;
        private Text gate, phaseLabel, reserveTitle;
        private RectTransform gateFill;
        private readonly List<Image> wavePips = new List<Image>();
        private readonly List<Text> laneForecast = new List<Text>();
        private Font font;
        private readonly List<HudTarget> targets = new List<HudTarget>();
        private string signature;
        private bool confirmMerge, confirmStart, confirmRestart, confirmRecycle;
        private string localMessage;
        public int Selected { get; private set; }
        public IReadOnlyList<HudTarget> Targets => targets;
        public void Build(BattleApplication application, BattleSceneView sceneView)
        {
            app = application; view = sceneView; font = Resources.Load<Font>("GameFont");
            var canvasGo = new GameObject("CombatHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); canvasGo.layer = 5; canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(540, 960); scaler.matchWidthOrHeight = 0;
            root = Panel(canvasGo.transform, "SafeArea", new Rect(0, 0, 1, 1), Color.clear);
            var safe = Screen.safeArea; root.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height); root.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            Panel(root, "Header", new Rect(0, .85f, 1, .15f), new Color(.105f, .09f, .16f));
            var footer = Panel(root, "Footer", new Rect(0, 0, 1, .325f), Color.white); CombatUiSkin.Decorate(footer, "Panel", Color.white, true, 3);
            Icon(root, "Shield", new Rect(.035f, .949f, .072f, .043f));
            Text(root, "符阵军团", new Rect(.12f, .946f, .6f, .05f), 27, TextAnchor.MiddleLeft, CombatUiSkin.Paper).fontStyle = FontStyle.Bold;
            Text(root, "遗迹庭院  ·  远征", new Rect(.12f, .931f, .55f, .025f), 12, TextAnchor.MiddleLeft, new Color(.7f, .67f, .78f));
            status = Text(root, "", new Rect(.035f, .894f, .42f, .038f), 20, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
            Icon(root, "Castle", new Rect(.53f, .894f, .06f, .04f));
            gate = Text(root, "", new Rect(.605f, .901f, .26f, .028f), 16);
            Panel(root, "GateBarBack", new Rect(.606f, .891f, .25f, .006f), new Color(.22f, .2f, .28f));
            gateFill = Panel(root, "GateHealth", new Rect(.606f, .891f, .25f, .006f), new Color(.37f, .82f, .66f));
            for (int i = 0; i < 12; i++) wavePips.Add(Panel(root, "Wave" + i, new Rect(.04f + i * .026f, .885f, .018f, .004f), CombatUiSkin.Gold).GetComponent<Image>());
            for (int i = 0; i < 3; i++)
            {
                var card = Panel(root, "LaneForecast" + i, new Rect(.025f + i * .323f, .851f, .303f, .027f), Color.white); CombatUiSkin.Decorate(card, "ButtonDark");
                laneForecast.Add(Text(card, "", new Rect(.04f, .03f, .92f, .94f), 11, TextAnchor.MiddleCenter));
            }
            forecast = Text(root, "", new Rect(.035f, .852f, .93f, .026f), 14, TextAnchor.MiddleCenter, CombatUiSkin.Gold);
            Panel(root, "InstructionBackground", new Rect(0, .81f, 1, .04f), new Color(.105f, .09f, .16f));
            var rule = Panel(root, "RuleBanner", new Rect(.09f, .817f, .82f, .031f), new Color(.12f, .12f, .18f, .9f));
            instruction = Text(rule, "", new Rect(.02f, 0, .96f, 1), 12, TextAnchor.MiddleCenter, CombatUiSkin.Paper);
            message = Text(root, "", new Rect(.035f, .32f, .93f, .032f), 12, TextAnchor.MiddleCenter, CombatUiSkin.Gold);
            phaseLabel = Text(root, "", new Rect(.06f, .296f, .88f, .026f), 14, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
            reserveTitle = Text(root, "", new Rect(.51f, .296f, .43f, .026f), 14, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
            dynamic = Panel(root, "Actions", new Rect(0, 0, 1, 1), Color.clear);
            Refresh();
        }
        private void Update()
        {
            if (app == null) return; var s = app.Session;
            status.text = $"第 {s.Wave:00} 波 <size=12>/ 12</size>";
            gate.text = "城门  " + s.GateHp + " / 10";
            gateFill.localScale = new Vector3(Mathf.Clamp01(s.GateHp / 10f), 1, 1);
            for (int i = 0; i < wavePips.Count; i++) wavePips[i].color = i < s.Wave ? CombatUiSkin.Gold : new Color(.28f, .26f, .35f);
            for (int i = 0; i < laneForecast.Count; i++)
            {
                laneForecast[i].transform.parent.gameObject.SetActive(s.FencesUp);
                int lane = i;
                laneForecast[i].text = new[] { "左路", "中路", "右路" }[i] + "  " + string.Join(" / ", s.Forecast.Where(e => e.Lane == lane).GroupBy(e => e.Kind).Select(g => BattleDefinitions.Name(g.Key) + "×" + g.Count()));
            }
            forecast.gameObject.SetActive(!s.FencesUp); forecast.text = "首领来袭  ·  石甲巨像  ·  三路协同迎战";
            phaseLabel.text = s.Phase == RunPhase.Preparation ? "召唤法阵" : s.Phase == RunPhase.Fighting ? "战术指挥  ·  本波剩余 " + s.Tactics + " 次" : s.Phase == RunPhase.Reward ? "远征奖励  ·  选择一项强化" : "远征战报";
            reserveTitle.gameObject.SetActive(s.Phase == RunPhase.Preparation);
            reserveTitle.text = "候补军团  " + s.Troops.Count(t => !t.Deployed) + "/6";
            instruction.text = s.Aim != AimKind.None ? "瞄准已暂停 · 点目标确认，或取消" : s.Phase == RunPhase.Preparation ? "横向三同出兵 · 点选或拖动兵到阵位" : s.Phase == RunPhase.Fighting ? s.Paused ? "已暂停 · 继续后战斗推进" : "自动战斗 · 每波一次集火或保护" : s.Phase == RunPhase.Reward ? "选择一项本局强化" : s.Phase == RunPhase.Won ? "远征胜利" : s.Phase == RunPhase.Lost ? "城门失守，可重新挑战" : "本波守住，倒下的军团下波恢复";
            message.text = localMessage ?? app.LastResult.Reason;
            string next = s.Phase + "/" + s.Spins + "/" + Selected + "/" + s.Aim + "/" + s.Paused + "/" + s.Speed + "/" + s.Tactics + "/" + confirmMerge + confirmStart + confirmRestart + confirmRecycle + "/" + string.Join(",", s.Troops.Select(t => $"{t.Id}:{t.Star}:{t.Branch}:{t.Lane}:{t.Row}")) + "/" + string.Join(",", s.Rewards);
            if (next != signature) { signature = next; Rebuild(); }
        }
        public void Refresh() { signature = null; }
        public void Select(int unitId)
        {
            Selected = unitId; confirmMerge = confirmStart = confirmRestart = confirmRecycle = false;
            var t = app.Session.Troops.FirstOrDefault(u => u.Id == unitId);
            localMessage = t == null ? null : BattleDefinitions.Name(t.Kind) + " " + t.Star + "星 · " + (t.Branch == SkillBranch.None ? "拖到空格部署或交换" : BattleDefinitions.Name(t.Branch)); Refresh();
        }
        public void Submit(BattleCommand command)
        {
            var result = app.Submit(command); localMessage = result.Reason;
            confirmStart = confirmMerge = confirmRestart = confirmRecycle = false;
            if (!app.Session.Troops.Any(t => t.Id == Selected)) Selected = 0;
            Refresh();
        }
        public void ClearSelection() { Selected = 0; localMessage = null; confirmMerge = confirmStart = confirmRestart = confirmRecycle = false; Refresh(); }
        public void CancelUi() { confirmMerge = confirmStart = confirmRestart = confirmRecycle = false; Refresh(); }
        private void Rebuild()
        {
            foreach (Transform child in dynamic) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            targets.Clear(); var s = app.Session;
            Button("restart", "重开", new Rect(.82f, .944f, .145f, .046f), () => { confirmRestart = true; Refresh(); });
            if (s.Phase == RunPhase.Preparation)
            {
                var reelPanel = Panel(dynamic, "Reels", new Rect(.03f, .11f, .435f, .18f), Color.white); CombatUiSkin.Decorate(reelPanel, "Panel", Color.white, true, 5);
                for (int row = 0; row < 3; row++) for (int col = 0; col < 3; col++)
                {
                    var kind = s.Reels[row * 3 + col]; bool match = s.Reels[row * 3] == s.Reels[row * 3 + 1] && s.Reels[row * 3] == s.Reels[row * 3 + 2];
                    var tile = Panel(reelPanel, "Symbol", new Rect(col / 3f + .02f, (2 - row) / 3f + .025f, .295f, .29f), Color.white);
                    CombatUiSkin.Decorate(tile, "Card", match ? new Color(1, .85f, .52f) : new Color(.8f, .8f, .94f));
                    Icon(tile, CombatUiSkin.TroopIcon((int)kind), new Rect(.15f, .12f, .7f, .76f), kind == TroopKind.Frost ? new Color(.55f, .9f, 1) : (Color?)null);
                }
                var reserve = s.Troops.Where(t => !t.Deployed).ToArray();
                for (int i = 0; i < 6; i++)
                {
                    var rect = new Rect(.51f + (i % 3) * .158f, .205f - (i / 3) * .09f, .148f, .082f);
                    if (i >= reserve.Length)
                    {
                        var empty = Panel(dynamic, "EmptyReserve", rect, Color.white); CombatUiSkin.Decorate(empty, "Card", new Color(.55f, .55f, .67f));
                        Text(empty, "+", new Rect(0, .1f, 1, .8f), 24, TextAnchor.MiddleCenter, new Color(.4f, .39f, .49f)); continue;
                    }
                    var t = reserve[i]; Button("unit_" + t.Id, BattleDefinitions.Name(t.Kind) + "\n" + new string('★', t.Star), rect, () => Select(t.Id), t.Id, t.Id == Selected);
                }
                Button("spin", "招募  ·  " + s.Spins + " 次", new Rect(.03f, .027f, .435f, .065f), () => Submit(new BattleCommand { Kind = CommandKind.Spin }));
                Button("start_wave", "开始战斗", new Rect(.51f, .027f, .46f, .065f), () => { if (s.Spins > 0) { confirmStart = true; Refresh(); } else Submit(new BattleCommand { Kind = CommandKind.StartWave }); });
                Text(dynamic, "横排三同出兵  ·  三名同职业同星可合成", new Rect(.04f, .004f, .92f, .02f), 10, TextAnchor.MiddleCenter, new Color(.6f, .57f, .67f));
                var selected = s.Troops.FirstOrDefault(t => t.Id == Selected);
                if (selected != null) SelectionPanel(selected);
                if (s.Pending.Count > 0) localMessage = "暂存" + s.Pending.Count + "兵：先部署、合成或遣散候补，再继续";
            }
            else if (s.Phase == RunPhase.Fighting)
            {
                Text(dynamic, "选定战术后点击战场目标，瞄准时暂停", new Rect(.06f, .249f, .88f, .034f), 13, TextAnchor.MiddleCenter, new Color(.73f, .7f, .8f));
                Button("aim_focus", "集火目标", new Rect(.05f, .128f, .42f, .104f), () => Submit(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Focus }));
                Button("aim_protect", "保护一路", new Rect(.53f, .128f, .42f, .104f), () => Submit(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Protect }));
                Button("pause", s.Paused ? "继续" : "暂停", new Rect(.05f, .035f, .27f, .06f), () => Submit(new BattleCommand { Kind = CommandKind.Pause, Flag = !s.Paused }));
                Button("speed", s.Speed + "倍速", new Rect(.37f, .035f, .27f, .06f), () => Submit(new BattleCommand { Kind = CommandKind.SetSpeed, Speed = s.Speed == 1 ? 2 : 1 }));
                Button("cancel_aim", "取消瞄准", new Rect(.69f, .035f, .27f, .06f), () => Submit(new BattleCommand { Kind = CommandKind.CancelAim }));
            }
            else if (s.Phase == RunPhase.Reward)
            {
                var options = Enum.GetValues(typeof(RewardKind)).Cast<RewardKind>().Where(r => !s.Rewards.Contains(r)).ToArray();
                for (int i = 0; i < options.Length; i++)
                {
                    var r = options[i]; float x = .03f + i * .32f;
                    Button("reward_" + r, BattleDefinitions.Name(r) + "\n" + BattleDefinitions.RewardDescription(r), new Rect(x, .06f, .30f, .23f), () => Submit(new BattleCommand { Kind = CommandKind.ChooseReward, Reward = r }));
                }
            }
            else
            {
                Text(dynamic, s.Phase == RunPhase.Won ? "守住了！十二波远征完成" : s.Phase == RunPhase.Lost ? "城门失守 · 调整阵容再试" : "本波结束 · 下一波重新补满招募次数", new Rect(.05f, .17f, .9f, .09f), 22, TextAnchor.MiddleCenter);
                if (s.Phase == RunPhase.WaveResult) Button("next_wave", "查看下一波", new Rect(.15f, .05f, .7f, .085f), () => { ClearSelection(); Submit(new BattleCommand { Kind = CommandKind.NextWave }); });
                else Button("new_run", "重新远征", new Rect(.15f, .05f, .7f, .085f), () => Submit(new BattleCommand { Kind = CommandKind.NewRun, Seed = BattleApplication.FreshSeed() }));
            }
            if (confirmMerge || confirmStart || confirmRestart || confirmRecycle) ConfirmPanel();
        }
        private void SelectionPanel(Troop unit)
        {
            var s = app.Session;
            var selectedPanel = Panel(dynamic, "SelectedUnit", new Rect(.035f, .365f, .93f, .14f), Color.white); CombatUiSkin.Decorate(selectedPanel, "Panel", Color.white, true, 4);
            Icon(dynamic, CombatUiSkin.TroopIcon((int)unit.Kind), new Rect(.07f, .453f, .07f, .041f));
            Text(dynamic, BattleDefinitions.Name(unit.Kind) + "  " + new string('★', unit.Star) + "  " + (unit.Branch == SkillBranch.None ? "" : BattleDefinitions.Name(unit.Branch)), new Rect(.16f, .46f, .73f, .035f), 18, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
            if (unit.Star >= 2 && unit.Branch == SkillBranch.None)
            {
                for (int i = 0; i < 2; i++) { var branch = (SkillBranch)(1 + (int)unit.Kind * 2 + i); Button("branch_" + branch, BattleDefinitions.Name(branch), new Rect(.06f + i * .45f, .38f, .41f, .065f), () => Submit(new BattleCommand { Kind = CommandKind.ChooseBranch, UnitId = unit.Id, Branch = branch })); }
                return;
            }
            Button("merge", "三兵合成", new Rect(.06f, .38f, .27f, .065f), () => { confirmMerge = true; Refresh(); });
            Button(unit.Deployed ? "recall" : "recycle", unit.Deployed ? "召回候补" : "遣散候补", new Rect(.365f, .38f, .27f, .065f), () => { if (unit.Deployed) Submit(new BattleCommand { Kind = CommandKind.Recall, UnitId = unit.Id }); else { confirmRecycle = true; Refresh(); } });
            Button("clear_selection", "关闭", new Rect(.67f, .38f, .27f, .065f), ClearSelection);
        }
        private void ConfirmPanel()
        {
            Panel(dynamic, "ConfirmBackdrop", new Rect(0, 0, 1, 1), new Color(0, 0, 0, .7f));
            var confirm = Panel(dynamic, "Confirm", new Rect(.07f, .35f, .86f, .31f), Color.white); CombatUiSkin.Decorate(confirm, "Panel", Color.white, true, 2);
            Text(confirm, confirmRestart ? "重新远征" : confirmStart ? "准备迎战" : confirmMerge ? "军团升星" : "遣散候补", new Rect(.08f, .78f, .84f, .14f), 23, TextAnchor.MiddleCenter, CombatUiSkin.Gold).fontStyle = FontStyle.Bold;
            var s = app.Session; var host = s.Troops.FirstOrDefault(t => t.Id == Selected);
            var donors = host == null ? Array.Empty<Troop>() : s.Troops.Where(t => t.Id != host.Id && t.Kind == host.Kind && t.Star == host.Star).OrderBy(t => t.Deployed).ThenBy(t => t.Id).Take(2).ToArray();
            string text = confirmRestart ? "重开会清空本局军团与奖励，确定？" : confirmStart ? "还有" + s.Spins + "次未转，提前开战会放弃这些次数。确定？" : confirmRecycle ? "遣散这名候补，本局不可找回。确定？" : host == null || donors.Length < 2 || host.Star >= 3 ? "需要三个同职业同星级兵，最高三星。" : $"保留{Location(host)}的位置与分支\n消耗{Location(donors[0])}、{Location(donors[1])}\n合成为 {host.Star + 1} 星";
            Text(dynamic, text, new Rect(.12f, .455f, .76f, .12f), 18, TextAnchor.MiddleCenter);
            Button("confirm_cancel", "取消", new Rect(.12f, .375f, .33f, .063f), CancelUi);
            Button("confirm_accept", "确认", new Rect(.55f, .375f, .33f, .063f), () =>
            {
                if (confirmRestart) { Selected = 0; Submit(new BattleCommand { Kind = CommandKind.NewRun, Seed = BattleApplication.FreshSeed() }); }
                else if (confirmStart) Submit(new BattleCommand { Kind = CommandKind.StartWave });
                else if (confirmRecycle && host != null) Submit(new BattleCommand { Kind = CommandKind.Recycle, UnitId = host.Id });
                else if (host != null && donors.Length == 2) Submit(new BattleCommand { Kind = CommandKind.Merge, UnitId = host.Id, OtherId = donors[0].Id, ThirdId = donors[1].Id });
                else CancelUi();
            });
        }
        public HudTarget Hit(Vector2 screen)
        {
            // 确认弹层仅允许弹层按钮；其他区域不穿透。
            bool modal = confirmMerge || confirmStart || confirmRestart || confirmRecycle;
            return targets.AsEnumerable().Reverse().FirstOrDefault(t => (!modal || t.Id.StartsWith("confirm_")) && RectTransformUtility.RectangleContainsScreenPoint(t.Rect, screen, null));
        }
        public bool BlocksWorld => confirmMerge || confirmStart || confirmRestart || confirmRecycle;
        private static string Location(Troop unit) => (unit.Deployed ? new[] { "左路", "中路", "右路" }[unit.Lane] + new[] { "前排", "中排", "后排" }[unit.Row] : "候补") + BattleDefinitions.Name(unit.Kind);
        private void Button(string id, string label, Rect rect, Action action, int unitId = 0, bool selected = false)
        {
            var p = Panel(dynamic, id, rect, Color.white);
            if (unitId > 0)
            {
                CombatUiSkin.Decorate(p, selected ? "CardGold" : "Card");
                var unit = app.Session.Troops.First(t => t.Id == unitId);
                Icon(p, CombatUiSkin.TroopIcon((int)unit.Kind), new Rect(.2f, .41f, .6f, .49f), unit.Kind == TroopKind.Frost ? new Color(.55f, .9f, 1) : (Color?)null);
                Text(p, BattleDefinitions.Name(unit.Kind), new Rect(.02f, .2f, .96f, .22f), 11, TextAnchor.MiddleCenter);
                Text(p, new string('★', unit.Star), new Rect(.02f, .02f, .96f, .2f), 12, TextAnchor.MiddleCenter, CombatUiSkin.Gold);
            }
            else if (id.StartsWith("reward_", StringComparison.Ordinal))
            {
                CombatUiSkin.Decorate(p, "Panel", Color.white, true, 4);
                Icon(p, id.EndsWith("Reinforcements", StringComparison.Ordinal) ? "Helmet" : id.EndsWith("WideStorm", StringComparison.Ordinal) ? "Storm" : "Frost", new Rect(.24f, .61f, .52f, .3f));
                string[] parts = label.Split('\n');
                Text(p, parts[0], new Rect(.05f, .43f, .9f, .15f), 17, TextAnchor.MiddleCenter, CombatUiSkin.Gold).fontStyle = FontStyle.Bold;
                Text(p, parts.Length > 1 ? parts[1] : "", new Rect(.08f, .08f, .84f, .31f), 12, TextAnchor.UpperCenter);
            }
            else
            {
                bool major = id == "spin" || id == "confirm_accept" || id == "next_wave" || id == "new_run";
                string skin = major ? "ButtonGold" : id == "start_wave" ? "ButtonGreen" : id.StartsWith("aim_", StringComparison.Ordinal) ? "ButtonBlue" : "ButtonGray";
                if (id.StartsWith("aim_", StringComparison.Ordinal) && app.Session.Tactics == 0) skin = "ButtonDark";
                CombatUiSkin.Decorate(p, skin);
                bool tactical = id.StartsWith("aim_", StringComparison.Ordinal);
                if (tactical) Icon(p, id == "aim_focus" ? "Target" : "Protect", new Rect(.07f, .19f, .24f, .62f));
                var text = Text(p, label, tactical ? new Rect(.33f, .1f, .62f, .8f) : new Rect(.04f, .09f, .92f, .82f), rect.height < .05f ? 12 : tactical ? 19 : 20, TextAnchor.MiddleCenter, major || id == "start_wave" ? CombatUiSkin.Ink : CombatUiSkin.Paper);
                text.fontStyle = FontStyle.Bold;
            }
            targets.Add(new HudTarget { Id = id, Rect = p, Click = action, UnitId = unitId });
        }
        private static Image Icon(Transform parent, string sprite, Rect rect, Color? color = null)
        {
            var p = Panel(parent, "Icon_" + sprite, rect, Color.clear);
            return CombatUiSkin.Decorate(p, sprite, color, false);
        }
        private static RectTransform Panel(Transform parent, string name, Rect rect, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5; var rt = go.GetComponent<RectTransform>(); rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(rect.xMin, rect.yMin); rt.anchorMax = new Vector2(rect.xMax, rect.yMax); rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (color.a > 0) { var image = go.AddComponent<Image>(); image.color = color; image.raycastTarget = false; }
            return rt;
        }
        private Text Text(Transform parent, string content, Rect rect, int size, TextAnchor align = TextAnchor.MiddleLeft, Color? color = null)
        {
            var p = Panel(parent, "Text", rect, Color.clear); var text = p.gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size; text.text = content; text.alignment = align; text.color = color ?? new Color(.91f, .94f, .92f); text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; return text;
        }
        public object Snapshot() => new
        {
            width = Screen.width, height = Screen.height, selected = Selected, modal = BlocksWorld,
            skin = CombatUiSkin.Snapshot(),
            buttons = targets.Where(t => t.Rect != null && t.Rect.gameObject.activeInHierarchy).Select(t => new { id = t.Id, unitId = t.UnitId, x = t.Center.x / Screen.width, y = t.Center.y / Screen.height, width = t.Rect.rect.width * t.Rect.lossyScale.x / Screen.width, height = t.Rect.rect.height * t.Rect.lossyScale.y / Screen.height }),
            cells = Enumerable.Range(0, 3).SelectMany(l => Enumerable.Range(0, 3).Select(r => new { lane = l, row = r, x = view.CellScreen(l, r).x / Screen.width, y = view.CellScreen(l, r).y / Screen.height })),
            troops = app.Session.Troops.Where(t => t.Deployed).Select(t => new { id = t.Id, x = view.UnitScreen(t.Id).x / Screen.width, y = view.UnitScreen(t.Id).y / Screen.height }),
            enemies = app.Session.Enemies.Where(e => e.Alive).Select(e => new { id = e.Id, lane = e.Lane, x = view.UnitScreen(e.Id).x / Screen.width, y = view.UnitScreen(e.Id).y / Screen.height }),
            camera = new { x = view.Camera.rect.x, y = view.Camera.rect.y, width = view.Camera.rect.width, height = view.Camera.rect.height }, message = message.text
            , presentation = view.Snapshot()
        };
    }
}
