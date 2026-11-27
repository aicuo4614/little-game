using Unity.Profiling;
using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Client.Battle;
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
    /// 1. 顶部展示关卡名、波次节点与人口；左右展示羁绊与伤害统计；
    /// 2. 选中场上单位不再弹出任何按钮面板，仅高亮脚底光标；拖回候补区直接召回；
    /// 3. 候补卡片支持拖动上阵与双击自动寻找空位上阵；
    /// 4. 老虎机十字中线滚动 → 闪白流光 → 3格汇聚合成光球 → 抛物线飞入目标：
    ///    - 新进候补的单位在飞行落地前先隐藏图标与计数，抵达槽位那一帧才真正显示并弹跳；
    ///    - 若新兵直接触发自动三合一升星，则飞行光球飞向升星宿主位置，绝不误飞向空候补格。
    /// </summary>
    public sealed partial class BattleHud : MonoBehaviour, IBattleView
    {
        private static readonly ProfilerMarker PerformanceMarker = new ProfilerMarker("LittleGame.Hud");
        private const float HeaderBottom = .85f;
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

        private StatusPanel statusPanel;
        private BattleApplication app;
        private BattleView view;
        private Camera uiCamera;
        private RectTransform root, dynamic, fxLayer, footer;
        private RectTransform reserveDropZone;
        private Image reserveDropBg;
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

        private Font font;
        private readonly List<HudTarget> targets = new List<HudTarget>();
        private ActionsPanel actionsPanel;
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

        public void Build(BattleApplication application, BattleView sceneView, ModuleWindowBase window)
        {
            app = application;
            view = sceneView;
            uiCamera = sceneView.HudCamera;
            font = Resources.Load<Font>("GameFont");
            radialRingSprite = CreateRadialRingSprite();
            var shineShader = Shader.Find("LittleGame/UI/SlotShine") ?? Resources.Load<Shader>("Shaders/UiSlotShine");
            if (shineShader != null) slotShineMat = new Material(shineShader);

            // 由 ModuleWindow 所在的分层根 Canvas 统一缩放和排序，避免嵌套根 Canvas 把 UI 缩成一团。
            AOTUiRoot.Instance.BindCamera(uiCamera);
            var host = (RectTransform)transform;
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.one;
            host.offsetMin = host.offsetMax = Vector2.zero;
            var canvasGo = new GameObject("CombatHUD", typeof(RectTransform));
            canvasGo.layer = 5;
            var content = (RectTransform)canvasGo.transform;
            content.SetParent(transform, false);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = content.offsetMax = Vector2.zero;

            root = Panel(canvasGo.transform, "SafeArea", new Rect(0, 0, 1, 1), Color.clear);
            var safe = Screen.safeArea;
            root.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            root.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);

            Panel(root, "Header", new Rect(0, HeaderBottom, 1, 1 - HeaderBottom), new Color(.105f, .09f, .16f));
            footer = Panel(root, "Footer", new Rect(0, 0, 1, .325f), Color.white);
            CombatUiSkin.Decorate(footer, "Panel", Color.white, true, 3);

            reserveDropZone = Panel(root, "ReserveDropZone", new Rect(.495f, .105f, .485f, .212f), new Color(0.28f, 0.85f, 0.65f, 0f));
            reserveDropBg = reserveDropZone.GetComponent<Image>();

            statusPanel = new StatusPanel(this);
            statusPanel.InternalInit(window, Panel(root, "StatusPanel", new Rect(0, 0, 1, 1), Color.clear));
            statusPanel.InternalOpen();
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

            actionsPanel = new ActionsPanel(this);
            actionsPanel.InternalInit(window, dynamic);
            actionsPanel.InternalOpen();
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
            using var performanceSample = PerformanceMarker.Auto();
            if (app == null) return;
            var s = app.Session;

            // 若离开准备阶段或发生重开，立即清理未播完的飞行动画并揭晓所有单位
            if (s.Phase != RunPhase.Preparation && (recruitFlyAnims.Count > 0 || hiddenReserveIds.Count > 0))
            {
                ClearRecruitFlyAnims(revealAll: true);
            }

            view?.SetSelectedUnit(Selected, s.Phase == RunPhase.Preparation);

            statusPanel.Tick(s);

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

            actionsPanel.Tick(s);

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

        public void Refresh() { actionsPanel?.Invalidate(); }

        public void Select(int unitId)
        {
            var t = app.Session.Troops.FirstOrDefault(u => u.Id == unitId);
            if (t == null || !t.Deployed)
            {
                Selected = 0;
                if (t != null) ShowTip("拖动或双击" + t.Definition.Name + "上阵", isError: false);
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

        public object Snapshot() => new
        {
            width = Screen.width,
            height = Screen.height,
            selected = Selected,
            modal = BlocksWorld,
            skin = CombatUiSkin.Snapshot(),
            panels = statusPanel.Snapshot(),
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
            statusPanel?.DisposeVisuals();
            CombatUiSkin.ReleasePortraits();
            ClosePanels();
            ClearRecruitFlyAnims(revealAll: false);
            if (slotShineMat != null) Destroy(slotShineMat);
            if (radialRingSprite != null) { Destroy(radialRingSprite.texture); Destroy(radialRingSprite); }
        }
    }
}
