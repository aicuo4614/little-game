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
        private static readonly Vector3[] CornerBuffer = new Vector3[4];
        public string Id;
        public RectTransform Rect;
        public Action Click;
        public int UnitId;
        public Camera UiCamera;

        public Rect ScreenRect
        {
            get
            {
                Rect.GetWorldCorners(CornerBuffer);
                var bl = RectTransformUtility.WorldToScreenPoint(UiCamera, CornerBuffer[0]);
                var tr = RectTransformUtility.WorldToScreenPoint(UiCamera, CornerBuffer[2]);
                return UnityEngine.Rect.MinMaxRect(Mathf.Min(bl.x, tr.x), Mathf.Min(bl.y, tr.y), Mathf.Max(bl.x, tr.x), Mathf.Max(bl.y, tr.y));
            }
        }

        public Vector2 Center => ScreenRect.center;
    }

    /// <summary>
    /// 横向无限远征战斗 HUD：
    /// 1. 顶部精简展示波次进度、自走棋人口经验圆环与存活军团数；
    /// 2. 选中场上单位不再弹出任何按钮面板，仅高亮脚底光标；拖回候补区直接召回；
    /// 3. 候补卡片支持拖动上阵与双击自动寻找空位上阵；
    /// 4. 老虎机十字中线滚动 → 闪白流光 → 3格汇聚合成光球 → 抛物线飞入目标：
    ///    - 新进候补的单位在飞行落地前先隐藏图标与计数，抵达槽位那一帧才真正显示并弹跳；
    ///    - 若新兵直接触发自动三合一升星，则飞行光球飞向升星宿主位置，绝不误飞向空候补格。
    /// </summary>
    public sealed class BattleHud : MonoBehaviour, IBattleView
    {
        private static readonly int ShinePosId = Shader.PropertyToID("_ShinePos");
        private static readonly int FlashIntensityId = Shader.PropertyToID("_FlashIntensity");

        private sealed class RecruitFlyAnim
        {
            public TroopKind Kind;
            public int[] SourceIndices;
            public RectTransform[] ConvergeIcons;
            public RectTransform MergedOrb;
            public RectTransform MergedHalo;
            public Vector2 CenterAnchor;
            public Vector2 TargetAnchor;
            public int RevealUnitId;
            public int TargetSlotIdx = -1;
            public float Delay;
            public float Elapsed;
            public float ConvergeDuration = 0.22f;
            public float FlyDuration = 0.36f;
            public bool BurstDone;
        }

        private BattleApplication app;
        private BattleSceneView view;
        private Camera uiCamera;
        private RectTransform root, dynamic, fxLayer;
        private RectTransform reserveDropZone;
        private Image reserveDropBg;
        private Text status, forecast, instruction;
        private Text squadText, phaseLabel, reserveTitle, popText;
        private Image popRingFill;
        private Sprite radialRingSprite;
        private Material slotShineMat;

        // 全局悬浮 Toast Tip 条
        private RectTransform toastRoot;
        private Image toastBg;
        private Text toastLabel;
        private CanvasGroup toastGroup;
        private float toastTimer;
        private float toastDuration = 1.6f;
        private bool toastIsError;
        private string toastText = "";

        private readonly List<Image> wavePips = new List<Image>();
        private Font font;
        private readonly List<HudTarget> targets = new List<HudTarget>();
        private string signature;
        private int lastHudWave = -1, lastHudAlive = -1, lastHudDeployed = -1, lastHudMaxDeployed = -1, lastHudPopExp = -1, lastHudTactics = -1, lastHudReserve = -1;
        private float lastHudMarch = -1f;
        private RunPhase lastHudPhase = (RunPhase)(-1);
        private AimKind lastHudAim = (AimKind)(-1);
        private bool lastHudPaused, lastHudEnraged, lastHudDropHighlight;
        private long lastCheckedSeq = -1;
        private int lastCheckedSelected = -1, lastCheckedHidden = -1;
        private bool confirmStart, confirmRestart, confirmRecycle;
        private bool reserveDropHighlighted;

        // 老虎机滚动、中奖闪光与合成飞入动画状态（含延迟揭晓）
        private readonly Image[] reelCardImages = new Image[9];
        private readonly Image[] reelIconImages = new Image[9];
        private readonly RectTransform[] reelTileRects = new RectTransform[9];
        private readonly RectTransform[] reserveSlotRects = new RectTransform[6];
        private readonly List<RecruitFlyAnim> recruitFlyAnims = new List<RecruitFlyAnim>();
        private readonly HashSet<int> knownReserveIds = new HashSet<int>();
        private readonly HashSet<int> hiddenReserveIds = new HashSet<int>();
        private readonly List<int> pendingRevealIds = new List<int>();
        private readonly List<int> pendingMergedHostIds = new List<int>();

        private float reelSpinTimer;
        private float reelWinFlashTimer;
        private float reelIconFlipTimer;
        private long lastObservedSpinSeq = -1;
        private bool reelWinRowMid, reelWinColMid;
        private TroopKind lastWonKind;
        private int pendingFlyCount;

        public int Selected { get; private set; }
        public IReadOnlyList<HudTarget> Targets => targets;

        public void Build(BattleApplication application, BattleSceneView sceneView)
        {
            app = application;
            view = sceneView;
            uiCamera = sceneView.HudCamera;
            font = Resources.Load<Font>("GameFont");
            radialRingSprite = CreateRadialRingSprite();
            var shineShader = Shader.Find("LittleGame/UI/SlotShine") ?? Resources.Load<Shader>("Shaders/UiSlotShine");
            if (shineShader != null) slotShineMat = new Material(shineShader);

            var canvasGo = new GameObject("CombatHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            if (uiCamera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCamera;
                canvas.planeDistance = 10f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(540, 960);
            scaler.matchWidthOrHeight = 0;

            root = Panel(canvasGo.transform, "SafeArea", new Rect(0, 0, 1, 1), Color.clear);
            var safe = Screen.safeArea;
            root.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            root.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);

            Panel(root, "Header", new Rect(0, .85f, 1, .15f), new Color(.105f, .09f, .16f));
            var footer = Panel(root, "Footer", new Rect(0, 0, 1, .325f), Color.white);
            CombatUiSkin.Decorate(footer, "Panel", Color.white, true, 3);

            reserveDropZone = Panel(root, "ReserveDropZone", new Rect(.495f, .105f, .485f, .212f), new Color(0.28f, 0.85f, 0.65f, 0f));
            reserveDropBg = reserveDropZone.GetComponent<Image>();

            Icon(root, "Shield", new Rect(.035f, .948f, .072f, .044f));
            Text(root, "符阵军团", new Rect(.12f, .946f, .37f, .05f), 26, TextAnchor.MiddleLeft, CombatUiSkin.Paper).fontStyle = FontStyle.Bold;
            Text(root, "无尽遗迹  ·  远征", new Rect(.12f, .930f, .37f, .025f), 12, TextAnchor.MiddleLeft, new Color(.7f, .67f, .78f));

            var popRoot = Panel(root, "PopRing", new Rect(.505f, .936f, .295f, .056f), Color.clear);
            CreateRingImage(popRoot, "PopRingBg", new Rect(0f, .04f, .27f, .92f), new Color(.36f, .34f, .48f, .95f), 1f);
            popRingFill = CreateRingImage(popRoot, "PopRingFill", new Rect(0f, .04f, .27f, .92f), CombatUiSkin.Gold, 0f);
            popText = Text(popRoot, "人口 0/6", new Rect(.29f, .04f, .71f, .92f), 15, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
            popText.fontStyle = FontStyle.Bold;

            status = Text(root, "", new Rect(.035f, .892f, .42f, .038f), 20, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
            squadText = Text(root, "", new Rect(.505f, .894f, .45f, .034f), 14, TextAnchor.MiddleLeft, new Color(.84f, .92f, .96f));
            for (int i = 0; i < 12; i++) wavePips.Add(Panel(root, "Wave" + i, new Rect(.04f + i * .026f, .884f, .018f, .004f), CombatUiSkin.Gold).GetComponent<Image>());

            var forecastCard = Panel(root, "ForecastBanner", new Rect(.03f, .852f, .94f, .028f), Color.white);
            CombatUiSkin.Decorate(forecastCard, "ButtonDark");
            forecast = Text(forecastCard, "", new Rect(.03f, .02f, .94f, .96f), 12, TextAnchor.MiddleCenter, CombatUiSkin.Gold);

            Panel(root, "InstructionBackground", new Rect(0, .81f, 1, .04f), new Color(.105f, .09f, .16f));
            var rule = Panel(root, "RuleBanner", new Rect(.06f, .817f, .88f, .031f), new Color(.12f, .12f, .18f, .9f));
            instruction = Text(rule, "", new Rect(.02f, 0, .96f, 1), 12, TextAnchor.MiddleCenter, CombatUiSkin.Paper);

            phaseLabel = Text(root, "", new Rect(.06f, .296f, .44f, .026f), 14, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
            reserveTitle = Text(root, "", new Rect(.51f, .296f, .43f, .026f), 14, TextAnchor.MiddleLeft, CombatUiSkin.Gold);
            dynamic = Panel(root, "Actions", new Rect(0, 0, 1, 1), Color.clear);

            fxLayer = Panel(root, "RecruitFxLayer", new Rect(0, 0, 1, 1), Color.clear);

            toastRoot = Panel(root, "ToastTip", new Rect(.10f, .748f, .80f, .048f), new Color(.58f, .11f, .14f, .94f));
            toastBg = toastRoot.GetComponent<Image>();
            toastGroup = toastRoot.gameObject.AddComponent<CanvasGroup>();
            toastGroup.blocksRaycasts = false;
            toastGroup.interactable = false;
            toastGroup.alpha = 0f;
            toastLabel = Text(toastRoot, "", new Rect(.04f, .06f, .92f, .88f), 15, TextAnchor.MiddleCenter, Color.white);
            toastLabel.fontStyle = FontStyle.Bold;
            toastRoot.gameObject.SetActive(false);

            SyncKnownReserveIds(app.Session);
            Refresh();
        }

        private void SyncKnownReserveIds(BattleSession s)
        {
            knownReserveIds.Clear();
            foreach (var t in s.Troops)
                if (!t.Deployed) knownReserveIds.Add(t.Id);
        }

        public bool IsPointInReserveArea(Vector2 screenPoint)
        {
            if (reserveDropZone == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(reserveDropZone, screenPoint, uiCamera);
        }

        public void SetReserveDropHighlight(bool active)
        {
            reserveDropHighlighted = active;
            if (reserveDropBg != null)
                reserveDropBg.color = active ? new Color(0.26f, 0.88f, 0.68f, 0.28f) : new Color(0.26f, 0.88f, 0.68f, 0f);
        }

        public void ShowTip(string text, bool isError)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            toastText = text;
            toastIsError = isError;
            toastDuration = isError ? 1.85f : 1.45f;
            toastTimer = toastDuration;
            if (toastRoot == null) return;
            toastRoot.gameObject.SetActive(true);
            toastBg.color = isError
                ? new Color(0.66f, 0.12f, 0.15f, 0.94f)
                : new Color(0.14f, 0.13f, 0.25f, 0.92f);
            toastLabel.color = isError
                ? new Color(1f, 0.92f, 0.92f)
                : CombatUiSkin.Gold;
            toastLabel.text = text;
            toastGroup.alpha = 1f;
            toastRoot.SetAsLastSibling();
        }

        private void Update()
        {
            if (app == null) return;
            var s = app.Session;

            // 若离开准备阶段或发生重开，立即清理未播完的飞行动画并揭晓所有单位
            if (s.Phase != RunPhase.Preparation && (recruitFlyAnims.Count > 0 || hiddenReserveIds.Count > 0))
            {
                ClearRecruitFlyAnims(revealAll: true);
            }

            view?.SetSelectedUnit(Selected, s.Phase == RunPhase.Preparation);

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
                if (!allTroops[i].Deployed && !hiddenReserveIds.Contains(allTroops[i].Id)) visibleReserveCount++;

            if (s.Phase != lastHudPhase || s.Tactics != lastHudTactics || s.Aim != lastHudAim || s.Paused != lastHudPaused || visibleReserveCount != lastHudReserve || reserveDropHighlighted != lastHudDropHighlight)
            {
                lastHudPhase = s.Phase;
                lastHudTactics = s.Tactics;
                lastHudAim = s.Aim;
                lastHudPaused = s.Paused;
                lastHudReserve = visibleReserveCount;
                lastHudDropHighlight = reserveDropHighlighted;
                phaseLabel.text = s.Phase == RunPhase.Preparation ? "十字法阵" : s.Phase == RunPhase.Fighting ? "战术指挥  ·  本波剩余 " + s.Tactics + " 次" : s.Phase == RunPhase.Reward ? "远征奖励  ·  选择一项强化" : "远征战报";
                reserveTitle.gameObject.SetActive(s.Phase == RunPhase.Preparation);
                reserveTitle.text = reserveDropHighlighted ? "松手召回至候补军团" : "候补军团  " + visibleReserveCount + "/6";
                reserveTitle.color = reserveDropHighlighted ? new Color(0.48f, 1f, 0.80f) : CombatUiSkin.Gold;
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

            // O(1) 读取最新 spin 序号，彻底消灭每帧拷贝 600 条事件
            long spinSeq = s.LastSpinSequence;
            if (spinSeq != lastObservedSpinSeq)
            {
                bool wasInit = lastObservedSpinSeq < 0;
                long prevSeq = Math.Max(0, lastObservedSpinSeq);
                lastObservedSpinSeq = spinSeq;

                if (!wasInit && spinSeq > 0)
                {
                    // 先把上一次尚未播完的飞行单位立刻揭晓，避免连续快点招募吞掉图标
                    ClearRecruitFlyAnims(revealAll: true);

                    pendingRevealIds.Clear();
                    pendingMergedHostIds.Clear();

                    // 找出本次 spin 新进入候补的单位 ID，先加入 hiddenReserveIds 等飞行落地后再显现
                    foreach (var t in s.Troops)
                    {
                        if (!t.Deployed && !knownReserveIds.Contains(t.Id))
                        {
                            pendingRevealIds.Add(t.Id);
                            hiddenReserveIds.Add(t.Id);
                        }
                    }

                    // 找出本次 spin 触发的自动合成宿主（若有新兵直接合成到了场上/候补宿主，飞行目标指向宿主）
                    foreach (var ev in s.EventsAfter(prevSeq))
                    {
                        if (ev.Type == "merged" && ev.Sequence <= spinSeq && ev.Source > 0)
                            pendingMergedHostIds.Add(ev.Source);
                    }

                    reelSpinTimer = 0.50f;
                    reelWinFlashTimer = 0f;
                    reelWinRowMid = s.LastWinRowMid;
                    reelWinColMid = s.LastWinColMid;
                    lastWonKind = s.Reels[4];
                    pendingFlyCount = (reelWinRowMid ? 1 : 0) + (reelWinColMid ? 1 : 0);
                }
                SyncKnownReserveIds(s);
            }
            else if (reelSpinTimer <= 0f && reelWinFlashTimer <= 0f && recruitFlyAnims.Count == 0)
            {
                // 无动画进行时清理无效隐藏 ID 并同步已知候补集合
                if (hiddenReserveIds.Count > 0) hiddenReserveIds.Clear();
                SyncKnownReserveIds(s);
            }

            if (signature == null || s.LastSequence != lastCheckedSeq || Selected != lastCheckedSelected || hiddenReserveIds.Count != lastCheckedHidden)
            {
                lastCheckedSeq = s.LastSequence;
                lastCheckedSelected = Selected;
                lastCheckedHidden = hiddenReserveIds.Count;
                string hiddenSig = hiddenReserveIds.Count > 0 ? string.Join(",", hiddenReserveIds.OrderBy(id => id)) : "";
                string next = s.Phase + "/" + s.Spins + "/" + Selected + "/" + s.Aim + "/" + s.Paused + "/" + s.Speed + "/" + s.Tactics
                    + "/" + s.MaxDeployed + "/" + s.PopExp + "/" + confirmStart + confirmRestart + confirmRecycle
                    + "/" + hiddenSig
                    + "/" + string.Join(",", s.Troops.Select(t => $"{t.Id}:{t.Star}:{t.Lane}:{t.Row}"))
                    + "/" + string.Join(",", s.Rewards) + "/" + string.Join("", s.Reels.Select(r => (int)r));
                if (next != signature) { signature = next; Rebuild(); }
            }

            UpdateSlotReelAnimation(s);
            UpdateRecruitFlyAnims(Time.unscaledDeltaTime);
            UpdateToastTip();
        }

        private void UpdateToastTip()
        {
            if (toastRoot == null || toastTimer <= 0f) return;
            toastTimer = Mathf.Max(0f, toastTimer - Time.unscaledDeltaTime);
            float elapsed = toastDuration - toastTimer;

            float shakeX = toastIsError && elapsed < 0.24f
                ? Mathf.Sin(elapsed * 52f) * (1f - elapsed / 0.24f) * 0.014f
                : 0f;
            toastRoot.anchorMin = new Vector2(0.10f + shakeX, 0.748f);
            toastRoot.anchorMax = new Vector2(0.90f + shakeX, 0.796f);

            float alpha = toastTimer < 0.35f ? Mathf.Clamp01(toastTimer / 0.35f) : 1f;
            toastGroup.alpha = alpha;
            if (toastTimer <= 0f) toastRoot.gameObject.SetActive(false);
        }

        private void UpdateSlotReelAnimation(BattleSession s)
        {
            if (s.Phase != RunPhase.Preparation) return;
            float dt = Time.unscaledDeltaTime;

            if (reelSpinTimer > 0f)
            {
                reelSpinTimer = Mathf.Max(0f, reelSpinTimer - dt);
                reelIconFlipTimer -= dt;
                bool flipTick = reelIconFlipTimer <= 0f;
                if (flipTick) reelIconFlipTimer = 0.055f;

                float elapsed = 0.50f - reelSpinTimer;
                float[] stopTimes = { 0.18f, 0.32f, 0.46f };

                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        int idx = row * 3 + col;
                        var icon = reelIconImages[idx];
                        var tile = reelTileRects[idx];
                        if (icon == null || tile == null) continue;

                        if (elapsed < stopTimes[col])
                        {
                            if (flipTick)
                            {
                                int fakeKind = ((int)(elapsed * 26f) + row + col * 2) % 4;
                                icon.sprite = CombatUiSkin.Sprite(CombatUiSkin.TroopIcon(fakeKind));
                                icon.color = fakeKind == (int)TroopKind.Frost ? new Color(.55f, .9f, 1f) : Color.white;
                            }
                            float bounce = 0.92f + 0.08f * Mathf.Sin(elapsed * 42f + col);
                            tile.localScale = new Vector3(1f, bounce, 1f);
                        }
                        else
                        {
                            var kind = s.Reels[idx];
                            icon.sprite = CombatUiSkin.Sprite(CombatUiSkin.TroopIcon((int)kind));
                            icon.color = kind == TroopKind.Frost ? new Color(.55f, .9f, 1f) : Color.white;
                            float settle = Mathf.Clamp01((elapsed - stopTimes[col]) / 0.10f);
                            float pop = 1f + Mathf.Sin(settle * Mathf.PI) * 0.12f;
                            tile.localScale = Vector3.one * pop;
                        }
                    }
                }

                if (reelSpinTimer <= 0f)
                {
                    for (int i = 0; i < 9; i++) if (reelTileRects[i] != null) reelTileRects[i].localScale = Vector3.one;
                    if (reelWinRowMid || reelWinColMid) reelWinFlashTimer = 0.42f;
                }
                return;
            }

            if (reelWinFlashTimer > 0f)
            {
                reelWinFlashTimer = Mathf.Max(0f, reelWinFlashTimer - dt);
                float progress = 1f - reelWinFlashTimer / 0.42f;
                float flash = Mathf.Sin(progress * Mathf.PI * 3f) * 0.5f + 0.5f;
                float shinePos = Mathf.Lerp(-0.4f, 1.4f, progress);
                if (slotShineMat != null)
                {
                    slotShineMat.SetFloat(ShinePosId, shinePos);
                    slotShineMat.SetFloat(FlashIntensityId, flash * (1f - progress * 0.35f) * 1.4f);
                }

                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        int idx = row * 3 + col;
                        bool wonCell = (reelWinRowMid && row == 1) || (reelWinColMid && col == 1);
                        if (!wonCell) continue;
                        if (reelCardImages[idx] != null && slotShineMat != null)
                            reelCardImages[idx].material = reelWinFlashTimer > 0.01f ? slotShineMat : null;
                        if (reelTileRects[idx] != null)
                        {
                            float sPop = 1f + Mathf.Sin(progress * Mathf.PI * 2f) * 0.11f * (1f - progress);
                            reelTileRects[idx].localScale = Vector3.one * sPop;
                        }
                    }
                }
                if (reelWinFlashTimer <= 0.01f)
                {
                    for (int i = 0; i < 9; i++)
                    {
                        if (reelCardImages[i] != null) reelCardImages[i].material = null;
                        if (reelTileRects[i] != null) reelTileRects[i].localScale = Vector3.one;
                    }
                    if (pendingFlyCount > 0)
                    {
                        LaunchRecruitConvergeAndFly(s);
                        pendingFlyCount = 0;
                    }
                }
            }
        }

        private void LaunchRecruitConvergeAndFly(BattleSession s)
        {
            if (fxLayer == null)
            {
                ClearRecruitFlyAnims(revealAll: true);
                return;
            }

            var linesToLaunch = new List<int[]>();
            if (reelWinRowMid) linesToLaunch.Add(new[] { 3, 4, 5 });
            if (reelWinColMid) linesToLaunch.Add(new[] { 1, 4, 7 });

            // 当前已可见候补数决定新单位落地槽位序号
            int visibleBefore = s.Troops.Count(t => !t.Deployed && !hiddenReserveIds.Contains(t.Id));
            Vector2 centerAnchor = ReelTileAnchorCenter(4);

            for (int li = 0; li < linesToLaunch.Count; li++)
            {
                int revealId = li < pendingRevealIds.Count ? pendingRevealIds[li] : 0;
                int slotIdx = -1;
                Vector2 targetAnchor;

                if (revealId > 0)
                {
                    // 正常进入候补军团：飞向对应候补空槽，落地后再显现卡片
                    slotIdx = Mathf.Clamp(visibleBefore + li, 0, 5);
                    targetAnchor = ReserveSlotAnchorCenter(slotIdx);
                }
                else if (pendingMergedHostIds.Count > 0)
                {
                    // 直接触发了自动 3 合 1 升星：飞向发生升星的宿主真实位置（场上英雄或候补宿主）
                    int hostId = pendingMergedHostIds[Mathf.Min(li, pendingMergedHostIds.Count - 1)];
                    var host = s.Troops.FirstOrDefault(t => t.Id == hostId);
                    if (host != null && host.Deployed)
                    {
                        Vector2 scr = view.UnitScreen(host.Id);
                        targetAnchor = new Vector2(
                            Mathf.Clamp01(scr.x / Mathf.Max(1f, Screen.width)),
                            Mathf.Clamp01(scr.y / Mathf.Max(1f, Screen.height)));
                    }
                    else
                    {
                        int hostReserveIdx = s.Troops.Where(t => !t.Deployed).ToList().FindIndex(t => t.Id == hostId);
                        slotIdx = Mathf.Clamp(hostReserveIdx >= 0 ? hostReserveIdx : 0, 0, 5);
                        targetAnchor = ReserveSlotAnchorCenter(slotIdx);
                    }
                }
                else
                {
                    slotIdx = Mathf.Clamp(visibleBefore + li, 0, 5);
                    targetAnchor = ReserveSlotAnchorCenter(slotIdx);
                }

                var anim = new RecruitFlyAnim
                {
                    Kind = lastWonKind,
                    SourceIndices = linesToLaunch[li],
                    ConvergeIcons = new RectTransform[3],
                    CenterAnchor = centerAnchor,
                    TargetAnchor = targetAnchor,
                    RevealUnitId = revealId,
                    TargetSlotIdx = slotIdx,
                    Delay = li * 0.10f
                };

                Color iconTint = lastWonKind == TroopKind.Frost ? new Color(0.65f, 0.94f, 1f) : Color.white;
                string iconKey = CombatUiSkin.TroopIcon((int)lastWonKind);

                for (int k = 0; k < 3; k++)
                {
                    Vector2 src = ReelTileAnchorCenter(anim.SourceIndices[k]);
                    var iconRoot = Panel(fxLayer, "RecruitConverge_" + li + "_" + k, new Rect(src.x - 0.045f, src.y - 0.032f, 0.09f, 0.064f), Color.clear);
                    Icon(iconRoot, iconKey, new Rect(0.08f, 0.08f, 0.84f, 0.84f), iconTint);
                    anim.ConvergeIcons[k] = iconRoot;
                }

                var orb = Panel(fxLayer, "RecruitMergedOrb_" + li, new Rect(centerAnchor.x - 0.055f, centerAnchor.y - 0.04f, 0.11f, 0.08f), Color.clear);
                var halo = CreateRingImage(orb, "OrbHalo", new Rect(0f, 0f, 1f, 1f), new Color(1f, 0.86f, 0.30f, 0.95f), 1f).rectTransform;
                Icon(orb, iconKey, new Rect(0.16f, 0.16f, 0.68f, 0.68f), iconTint);
                orb.gameObject.SetActive(false);

                anim.MergedOrb = orb;
                anim.MergedHalo = halo;
                recruitFlyAnims.Add(anim);
            }
            fxLayer.SetAsLastSibling();
            if (toastRoot != null) toastRoot.SetAsLastSibling();
        }

        private void UpdateRecruitFlyAnims(float dt)
        {
            if (recruitFlyAnims.Count > 0)
            {
                for (int i = recruitFlyAnims.Count - 1; i >= 0; i--)
                {
                    var a = recruitFlyAnims[i];
                    if (a.Delay > 0f)
                    {
                        a.Delay -= dt;
                        continue;
                    }

                    a.Elapsed += dt;
                    if (a.Elapsed < a.ConvergeDuration)
                    {
                        float t = Mathf.Clamp01(a.Elapsed / a.ConvergeDuration);
                        float ease = t * t * (3f - 2f * t);
                        for (int k = 0; k < 3; k++)
                        {
                            var rt = a.ConvergeIcons[k];
                            if (rt == null) continue;
                            Vector2 src = ReelTileAnchorCenter(a.SourceIndices[k]);
                            Vector2 p = Vector2.Lerp(src, a.CenterAnchor, ease);
                            rt.anchorMin = p - new Vector2(0.045f, 0.032f);
                            rt.anchorMax = p + new Vector2(0.045f, 0.032f);
                            rt.localScale = Vector3.one * Mathf.Lerp(1.05f, 0.65f, ease);
                        }
                        continue;
                    }

                    if (!a.BurstDone)
                    {
                        a.BurstDone = true;
                        for (int k = 0; k < 3; k++)
                            if (a.ConvergeIcons[k] != null) Destroy(a.ConvergeIcons[k].gameObject);
                        if (a.MergedOrb != null) a.MergedOrb.gameObject.SetActive(true);
                    }

                    float flyT = Mathf.Clamp01((a.Elapsed - a.ConvergeDuration) / a.FlyDuration);
                    float flyEase = 1f - Mathf.Pow(1f - flyT, 2.2f);
                    if (a.MergedOrb != null)
                    {
                        Vector2 lin = Vector2.Lerp(a.CenterAnchor, a.TargetAnchor, flyEase);
                        float arcY = Mathf.Sin(flyT * Mathf.PI) * 0.135f;
                        Vector2 p = lin + new Vector2(0f, arcY);
                        a.MergedOrb.anchorMin = p - new Vector2(0.055f, 0.04f);
                        a.MergedOrb.anchorMax = p + new Vector2(0.055f, 0.04f);
                        float popScale = flyT < 0.25f
                            ? Mathf.Lerp(1.38f, 1.08f, flyT / 0.25f)
                            : Mathf.Lerp(1.08f, 0.78f, (flyT - 0.25f) / 0.75f);
                        a.MergedOrb.localScale = Vector3.one * popScale;
                        if (a.MergedHalo != null) a.MergedHalo.localRotation = Quaternion.Euler(0f, 0f, -flyT * 420f);
                    }

                    if (flyT >= 1f)
                    {
                        if (a.MergedOrb != null) Destroy(a.MergedOrb.gameObject);
                        int arrivedSlot = a.TargetSlotIdx;
                        // 动画抵达结束：此时才真正显现候补图标并触发槽位弹跳！
                        if (a.RevealUnitId > 0 && hiddenReserveIds.Remove(a.RevealUnitId))
                        {
                            signature = null;
                            Rebuild();
                            Canvas.ForceUpdateCanvases();
                        }
                        if (arrivedSlot >= 0 && arrivedSlot < reserveSlotRects.Length && reserveSlotRects[arrivedSlot] != null)
                        {
                            reserveSlotRects[arrivedSlot].localScale = Vector3.one * 1.16f;
                        }
                        recruitFlyAnims.RemoveAt(i);
                    }
                }
            }

            // 候补槽位弹跳平滑回弹至 1.0
            for (int sIdx = 0; sIdx < reserveSlotRects.Length; sIdx++)
            {
                var rt = reserveSlotRects[sIdx];
                if (rt != null && rt.localScale.x > 1.001f)
                    rt.localScale = Vector3.Lerp(rt.localScale, Vector3.one, Mathf.Clamp01(dt * 14f));
            }
        }

        private void ClearRecruitFlyAnims(bool revealAll)
        {
            for (int i = 0; i < recruitFlyAnims.Count; i++)
            {
                var a = recruitFlyAnims[i];
                if (a.ConvergeIcons != null)
                    for (int k = 0; k < a.ConvergeIcons.Length; k++)
                        if (a.ConvergeIcons[k] != null) Destroy(a.ConvergeIcons[k].gameObject);
                if (a.MergedOrb != null) Destroy(a.MergedOrb.gameObject);
            }
            recruitFlyAnims.Clear();
            pendingFlyCount = 0;
            if (revealAll && hiddenReserveIds.Count > 0)
            {
                hiddenReserveIds.Clear();
                Refresh();
            }
        }

        private static Vector2 ReelTileAnchorCenter(int idx)
        {
            int row = idx / 3;
            int col = idx % 3;
            float rx = 0.03f + (col / 3f + 0.02f + 0.1475f) * 0.435f;
            float ry = 0.11f + ((2 - row) / 3f + 0.025f + 0.145f) * 0.18f;
            return new Vector2(rx, ry);
        }

        private static Vector2 ReserveSlotAnchorCenter(int slotIdx)
        {
            int i = Mathf.Clamp(slotIdx, 0, 5);
            float x = 0.51f + (i % 3) * 0.158f + 0.074f;
            float y = 0.205f - (i / 3) * 0.09f + 0.041f;
            return new Vector2(x, y);
        }

        public void Refresh() { signature = null; }

        public void Select(int unitId)
        {
            var t = app.Session.Troops.FirstOrDefault(u => u.Id == unitId);
            if (t == null || !t.Deployed)
            {
                Selected = 0;
                if (t != null) ShowTip("拖动或双击" + BattleDefinitions.Name(t.Kind) + "上阵", isError: false);
                Refresh();
                return;
            }
            Selected = unitId;
            confirmStart = confirmRestart = confirmRecycle = false;
            Refresh();
        }

        public void Submit(BattleCommand command)
        {
            if (command.Kind == CommandKind.NewRun)
            {
                ClearRecruitFlyAnims(revealAll: true);
                lastObservedSpinSeq = 0;
                knownReserveIds.Clear();
            }
            var result = app.Submit(command);
            confirmStart = confirmRestart = confirmRecycle = false;
            var sel = app.Session.Troops.FirstOrDefault(t => t.Id == Selected);
            if (sel == null || !sel.Deployed) Selected = 0;

            if (!result.Accepted)
            {
                ShowTip(result.Reason, isError: true);
            }
            else if (command.Kind == CommandKind.Spin || command.Kind == CommandKind.Merge || command.Kind == CommandKind.ChooseReward || command.Kind == CommandKind.Recall)
            {
                ShowTip(result.Reason, isError: false);
            }
            Refresh();
        }

        public void ClearSelection() { Selected = 0; confirmStart = confirmRestart = confirmRecycle = false; Refresh(); }
        public void CancelUi() { confirmStart = confirmRestart = confirmRecycle = false; Refresh(); }

        private void Rebuild()
        {
            foreach (Transform child in dynamic) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            targets.Clear();
            Array.Clear(reelCardImages, 0, 9);
            Array.Clear(reelIconImages, 0, 9);
            Array.Clear(reelTileRects, 0, 9);
            Array.Clear(reserveSlotRects, 0, 6);

            var s = app.Session;
            Button("restart", "重开", new Rect(.82f, .944f, .145f, .046f), () => { confirmRestart = true; Refresh(); });

            if (s.Phase == RunPhase.Preparation)
            {
                var reelPanel = Panel(dynamic, "Reels", new Rect(.03f, .11f, .435f, .18f), Color.white);
                CombatUiSkin.Decorate(reelPanel, "Panel", Color.white, true, 5);

                Panel(reelPanel, "CrossRowTrack", new Rect(.015f, .345f, .97f, .31f), new Color(1f, 0.82f, 0.28f, 0.16f));
                Panel(reelPanel, "CrossColTrack", new Rect(.34f, .015f, .32f, .97f), new Color(1f, 0.82f, 0.28f, 0.16f));

                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        int idx = row * 3 + col;
                        var kind = s.Reels[idx];
                        bool onCross = row == 1 || col == 1;
                        bool wonCell = (s.LastWinRowMid && row == 1) || (s.LastWinColMid && col == 1);
                        var tile = Panel(reelPanel, "Symbol_" + idx, new Rect(col / 3f + .02f, (2 - row) / 3f + .025f, .295f, .29f), Color.white);
                        Color cardTint = wonCell
                            ? new Color(1f, 0.90f, 0.45f)
                            : onCross
                                ? new Color(0.95f, 0.90f, 0.72f)
                                : new Color(0.55f, 0.56f, 0.68f);
                        CombatUiSkin.Decorate(tile, "Card", cardTint);
                        var iconImg = Icon(tile, CombatUiSkin.TroopIcon((int)kind), new Rect(.15f, .12f, .7f, .76f),
                            kind == TroopKind.Frost ? new Color(.55f, .9f, 1f) : (Color?)null);

                        reelTileRects[idx] = tile;
                        reelCardImages[idx] = tile.GetComponent<Image>();
                        reelIconImages[idx] = iconImg;
                    }
                }

                // 仅显示已落地揭晓的候补单位（飞行途中隐藏，抵达后立即显现）
                var reserve = s.Troops.Where(t => !t.Deployed && !hiddenReserveIds.Contains(t.Id)).ToArray();
                for (int i = 0; i < 6; i++)
                {
                    var rect = new Rect(.51f + (i % 3) * .158f, .205f - (i / 3) * .09f, .148f, .082f);
                    if (i >= reserve.Length)
                    {
                        var empty = Panel(dynamic, "EmptyReserve", rect, Color.white);
                        CombatUiSkin.Decorate(empty, "Card", new Color(.55f, .55f, .67f));
                        Text(empty, "+", new Rect(0, .1f, 1, .8f), 24, TextAnchor.MiddleCenter, new Color(.4f, .39f, .49f));
                        reserveSlotRects[i] = empty;
                        continue;
                    }
                    var t = reserve[i];
                    var btnRect = Button("unit_" + t.Id, BattleDefinitions.Name(t.Kind) + "\n" + new string('★', t.Star), rect,
                        () => ShowTip("拖动或双击" + BattleDefinitions.Name(t.Kind) + "上阵", isError: false), t.Id, t.Star >= 2);
                    reserveSlotRects[i] = btnRect;
                }

                Button("spin", "招募  ·  " + s.Spins + " 次", new Rect(.03f, .027f, .435f, .065f), () => Submit(new BattleCommand { Kind = CommandKind.Spin }));
                Button("start_wave", "开始战斗", new Rect(.51f, .027f, .46f, .065f), () => { if (s.Spins > 0) { confirmStart = true; Refresh(); } else Submit(new BattleCommand { Kind = CommandKind.StartWave }); });
                Text(dynamic, "十字三同出兵 · 拖动或双击候补上阵 · 拖回候补区召回", new Rect(.04f, .004f, .92f, .02f), 10, TextAnchor.MiddleCenter, new Color(.6f, .57f, .67f));

                if (s.Pending.Count > 0) ShowTip("暂存" + s.Pending.Count + "兵：先部署或遣散候补再继续", isError: true);
            }
            else if (s.Phase == RunPhase.Fighting)
            {
                Text(dynamic, "选定战术后点击战场目标或区域，瞄准时暂停", new Rect(.06f, .249f, .88f, .034f), 13, TextAnchor.MiddleCenter, new Color(.73f, .7f, .8f));
                Button("aim_focus", "集火目标", new Rect(.05f, .128f, .42f, .104f), () => Submit(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Focus }));
                Button("aim_protect", "护盾保护", new Rect(.53f, .128f, .42f, .104f), () => Submit(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Protect }));
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
                Text(dynamic, s.Phase == RunPhase.Won ? "大捷！十二波无尽远征全胜" : s.Phase == RunPhase.Lost ? "全军覆没 · 调整阵容再战" : "本波告捷 · 全员满血恢复，人口经验 +2", new Rect(.05f, .17f, .9f, .09f), 20, TextAnchor.MiddleCenter);
                if (s.Phase == RunPhase.WaveResult) Button("next_wave", "继续推进", new Rect(.15f, .05f, .7f, .085f), () => { ClearSelection(); Submit(new BattleCommand { Kind = CommandKind.NextWave }); });
                else Button("new_run", "重新远征", new Rect(.15f, .05f, .7f, .085f), () => Submit(new BattleCommand { Kind = CommandKind.NewRun, Seed = BattleApplication.FreshSeed() }));
            }
            if (confirmStart || confirmRestart || confirmRecycle) ConfirmPanel();
            if (fxLayer != null) fxLayer.SetAsLastSibling();
            if (toastRoot != null) toastRoot.SetAsLastSibling();
        }

        private void ConfirmPanel()
        {
            Panel(dynamic, "ConfirmBackdrop", new Rect(0, 0, 1, 1), new Color(0, 0, 0, .7f));
            var confirm = Panel(dynamic, "Confirm", new Rect(.07f, .35f, .86f, .31f), Color.white);
            CombatUiSkin.Decorate(confirm, "Panel", Color.white, true, 2);
            Text(confirm, confirmRestart ? "重新远征" : confirmStart ? "准备迎战" : "遣散候补", new Rect(.08f, .78f, .84f, .14f), 23, TextAnchor.MiddleCenter, CombatUiSkin.Gold).fontStyle = FontStyle.Bold;
            var s = app.Session;
            var host = s.Troops.FirstOrDefault(t => t.Id == Selected);
            string text = confirmRestart ? "重开会清空本局军团与奖励，确定？" : confirmStart ? "还有" + s.Spins + "次未转，提前开战会放弃这些次数。确定？" : "遣散这名候补，本局不可找回。确定？";
            Text(dynamic, text, new Rect(.12f, .455f, .76f, .12f), 18, TextAnchor.MiddleCenter);
            Button("confirm_cancel", "取消", new Rect(.12f, .375f, .33f, .063f), CancelUi);
            Button("confirm_accept", "确认", new Rect(.55f, .375f, .33f, .063f), () =>
            {
                if (confirmRestart) { Selected = 0; Submit(new BattleCommand { Kind = CommandKind.NewRun, Seed = BattleApplication.FreshSeed() }); }
                else if (confirmStart) Submit(new BattleCommand { Kind = CommandKind.StartWave });
                else if (confirmRecycle && host != null) Submit(new BattleCommand { Kind = CommandKind.Recycle, UnitId = host.Id });
                else CancelUi();
            });
        }

        public HudTarget Hit(Vector2 screen)
        {
            if (BlocksWorld)
            {
                var modal = targets.LastOrDefault(t => (t.Id == "confirm_cancel" || t.Id == "confirm_accept") && RectTransformUtility.RectangleContainsScreenPoint(t.Rect, screen, uiCamera));
                return modal ?? new HudTarget { Id = "modal_block", Click = () => { } };
            }
            return targets.LastOrDefault(t => RectTransformUtility.RectangleContainsScreenPoint(t.Rect, screen, uiCamera));
        }

        public bool BlocksWorld => confirmStart || confirmRestart || confirmRecycle;

        private RectTransform Button(string id, string label, Rect area, Action click, int unitId = 0, bool accent = false)
        {
            string sprite = id.StartsWith("unit_") ? (accent ? "CardGold" : "Card") : id == "start_wave" || id == "confirm_accept" || id == "next_wave" || id == "new_run" ? "ButtonBlue" : id == "spin" || id.StartsWith("reward_") ? "ButtonGold" : id.StartsWith("aim_") ? (app.Session.Tactics > 0 ? "ButtonBlue" : "ButtonDark") : "ButtonGray";
            Color tint = id.StartsWith("unit_") ? (accent ? new Color(1, .95f, .72f) : new Color(.9f, .9f, 1)) : id == "spin" && app.Session.Spins <= 0 ? new Color(.55f, .55f, .62f) : Color.white;
            var rect = Panel(dynamic, "Btn_" + id, area, Color.white);
            CombatUiSkin.Decorate(rect, sprite, tint);
            var textColor = sprite == "ButtonGold" ? new Color(.22f, .12f, .04f) : id.StartsWith("unit_") ? CombatUiSkin.Paper : Color.white;
            bool iconBtn = id == "aim_focus" || id == "aim_protect" || id.StartsWith("unit_");
            if (id.StartsWith("unit_"))
            {
                var unit = app.Session.Troops.FirstOrDefault(u => u.Id == unitId);
                if (unit != null) Icon(rect, CombatUiSkin.TroopIcon((int)unit.Kind), new Rect(.27f, .4f, .46f, .52f), unit.Kind == TroopKind.Frost ? new Color(.55f, .9f, 1) : (Color?)null);
            }
            else if (id == "aim_focus" || id == "aim_protect") Icon(rect, id == "aim_focus" ? "Target" : "Protect", new Rect(.08f, .24f, .26f, .52f));
            var text = Text(rect, label, id.StartsWith("unit_") ? new Rect(.04f, .02f, .92f, .44f) : iconBtn ? new Rect(.35f, .05f, .61f, .9f) : new Rect(.05f, .05f, .9f, .9f), id.StartsWith("unit_") ? 10 : id == "spin" || id == "start_wave" || id.StartsWith("aim_") ? 19 : 14, iconBtn && !id.StartsWith("unit_") ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, textColor);
            if (id == "spin" || id == "start_wave" || id == "confirm_accept" || id.StartsWith("aim_")) text.fontStyle = FontStyle.Bold;
            targets.Add(new HudTarget { Id = id, Rect = rect, Click = click, UnitId = unitId, UiCamera = uiCamera });
            return rect;
        }

        private Image Icon(Transform parent, string key, Rect area, Color? tint = null)
        {
            var go = new GameObject("Icon_" + key, typeof(RectTransform), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = area.min; rect.anchorMax = area.max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.sprite = CombatUiSkin.Sprite(key);
            image.preserveAspect = true;
            image.color = tint ?? Color.white;
            image.raycastTarget = false;
            return image;
        }

        private Image CreateRingImage(Transform parent, string name, Rect area, Color color, float fillAmount)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = area.min; rect.anchorMax = area.max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = radialRingSprite;
            img.preserveAspect = true;
            img.color = color;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.fillAmount = fillAmount;
            img.raycastTarget = false;
            return img;
        }

        private static Sprite CreateRadialRingSprite()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float center = (size - 1) * 0.5f;
            float outer = size * 0.46f;
            float inner = size * 0.30f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float aOuter = Mathf.Clamp01(outer - d + 1f);
                    float aInner = Mathf.Clamp01(d - inner + 1f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, aOuter * aInner));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private RectTransform Panel(Transform parent, string name, Rect area, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = area.min; rect.anchorMax = area.max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = false;
            return rect;
        }

        private Text Text(Transform parent, string content, Rect area, int size, TextAnchor align = TextAnchor.MiddleCenter, Color? color = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = area.min; rect.anchorMax = area.max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.text = content; text.fontSize = size; text.alignment = align;
            text.color = color ?? new Color(.95f, .96f, .91f);
            text.font = font; text.raycastTarget = false;
            return text;
        }

        public object Snapshot() => new
        {
            width = Screen.width,
            height = Screen.height,
            selected = Selected,
            modal = BlocksWorld,
            skin = CombatUiSkin.Snapshot(),
            tip = new { text = toastText, isError = toastIsError, active = toastTimer > 0f },
            reserveDropArea = reserveDropZone != null ? new { x = 0.7375f, y = 0.211f } : null,
            recruitAnimating = reelSpinTimer > 0f || reelWinFlashTimer > 0f || recruitFlyAnims.Count > 0,
            buttons = targets.Select(t =>
            {
                var r = t.ScreenRect;
                return new
                {
                    id = t.Id,
                    unitId = t.UnitId,
                    x = r.center.x / Mathf.Max(1f, Screen.width),
                    y = r.center.y / Mathf.Max(1f, Screen.height),
                    width = r.width / Mathf.Max(1f, Screen.width),
                    height = r.height / Mathf.Max(1f, Screen.height)
                };
            }).ToArray(),
            cells = Enumerable.Range(0, 3).SelectMany(l => Enumerable.Range(0, 3).Select(r => new { lane = l, row = r, x = view.CellScreen(l, r).x / Screen.width, y = view.CellScreen(l, r).y / Screen.height })).ToArray(),
            troops = app.Session.Troops.Where(t => t.Deployed).Select(t => new { id = t.Id, x = view.UnitScreen(t.Id).x / Screen.width, y = view.UnitScreen(t.Id).y / Screen.height }).ToArray(),
            enemies = app.Session.Enemies.Where(e => e.Alive).Select(e => new { id = e.Id, x = view.UnitScreen(e.Id).x / Screen.width, y = view.UnitScreen(e.Id).y / Screen.height }).ToArray(),
            camera = new { x = view.Camera.rect.x, y = view.Camera.rect.y, width = view.Camera.rect.width, height = view.Camera.rect.height },
            message = toastText,
            presentation = view.Snapshot()
        };

        private void OnDestroy()
        {
            ClearRecruitFlyAnims(revealAll: false);
            if (slotShineMat != null) Destroy(slotShineMat);
        }
    }
}
