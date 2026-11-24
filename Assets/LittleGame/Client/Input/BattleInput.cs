using System.Linq;
using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client
{
    /// <summary>
    /// 鼠标、触屏与 MCP pointer 共用输入入口：
    /// 1. 拖拽场上或候补单位时：立即在战场地面显现整体白色布阵框，同时在高于 UI 的 3D 拖拽层拎起 1:1 同大小、面向右(+X)的真实 3D 模型；
    /// 2. 在白框内自由松手即落位（贴近已有单位自动替换），满员拖入白框空处先轻踩再抛物线弹回 + 红色 Tip；
    /// 3. 拖动超出白色布阵框松手：弹出红色 Tip 提示拖拽失败，候补单位抛物线弹回原槽、场上单位留在原位。
    /// </summary>
    public sealed class BattleInput : MonoBehaviour
    {
        private BattleApplication app;
        private BattleSceneView view;
        private BattleHud hud;
        private Vector2 start;
        private bool held, dragged, ghostStarted;
        private int heldUnit, finger = -1;
        private string heldButton;
        private RectTransform pressedVisual;

        public void Bind(BattleApplication application, BattleSceneView scene, BattleHud ui)
        {
            app = application;
            view = scene;
            hud = ui;
        }

        private void Update()
        {
            if (app == null) return;
            if (UnityEngine.Input.touchCount > 1) { Cancel(); return; }
            if (UnityEngine.Input.touchCount == 1)
            {
                var touch = UnityEngine.Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began) { Pointer("begin", touch.position); finger = touch.fingerId; }
                else if (touch.fingerId == finger && touch.phase == TouchPhase.Moved) Pointer("move", touch.position);
                else if (touch.fingerId == finger && touch.phase == TouchPhase.Ended) { Pointer("end", touch.position); finger = -1; }
                else if (touch.phase == TouchPhase.Canceled) Cancel();
                return;
            }
            if (UnityEngine.Input.GetMouseButtonDown(0)) Pointer("begin", UnityEngine.Input.mousePosition);
            if (UnityEngine.Input.GetMouseButton(0)) Pointer("move", UnityEngine.Input.mousePosition);
            if (UnityEngine.Input.GetMouseButtonUp(0)) Pointer("end", UnityEngine.Input.mousePosition);
        }

        public void Pointer(string phase, Vector2 point)
        {
            if (phase == "cancel") { Cancel(); return; }
            if (phase == "begin")
            {
                Cancel();
                held = true;
                start = point;
                var hit = hud.Hit(point);
                heldButton = hit?.Id;
                heldUnit = hit?.UnitId ?? 0;
                pressedVisual = hit?.Rect;
                if (pressedVisual != null) pressedVisual.localScale = Vector3.one * .96f;
                if (hit == null && !hud.BlocksWorld && app.Session.Phase == RunPhase.Preparation)
                    heldUnit = view.HitUnit(point, false);
                return;
            }

            if (!held) return;
            float threshold = Mathf.Max(8f, Screen.width * .015f);
            if (Vector2.Distance(point, start) > threshold) dragged = true;

            if (phase == "move")
            {
                if (dragged && heldUnit > 0 && !hud.BlocksWorld && app.Session.Phase == RunPhase.Preparation)
                {
                    var troop = app.Session.Troops.FirstOrDefault(t => t.Id == heldUnit);
                    if (troop != null)
                    {
                        if (!ghostStarted)
                        {
                            ghostStarted = true;
                            view.DragOverlay?.BeginDrag(troop, point);
                        }
                        else
                        {
                            view.DragOverlay?.UpdateDrag(point);
                        }

                        // 只要起拖（包括从候补 UI 拖出），立即显示整体白色布阵框
                        bool hasGround = view.ScreenToGround(point, out var ground, requireViewport: false);
                        bool inViewport = BattleSceneView.IsInBattleViewport(point);
                        bool insideBox = hasGround && inViewport && BattleDefinitions.IsInsideDeployBox(ground.x, ground.z);
                        bool occupied = insideBox && app.Session.Troops.Any(t =>
                            t.Id != troop.Id && t.Deployed &&
                            Vector2.Distance(new Vector2(t.HomeX, t.HomeZ), new Vector2(ground.x, ground.z)) <= 0.78f);
                        bool canPlace = insideBox && (troop.Deployed || occupied || app.Session.DeployedCount < app.Session.MaxDeployed);
                        view.SetDragZoneVisual(true, hasGround ? ground : Vector3.zero, insideBox, canPlace);
                    }
                }
                return;
            }

            if (phase != "end") return;
            var button = heldButton;
            int unit = heldUnit;
            bool wasDragged = dragged;
            Vector2 dragOrigin = start;
            bool hadGhost = ghostStarted;
            ClearHoldState(keepGhost: hadGhost);
            view.SetDragZoneVisual(false, Vector3.zero, false, false);

            if (wasDragged)
            {
                var troop = app.Session.Troops.FirstOrDefault(t => t.Id == unit);
                if (unit > 0 && troop != null && !hud.BlocksWorld && app.Session.Phase == RunPhase.Preparation)
                {
                    if (view.ScreenToGround(point, out var dropWorld, requireViewport: true))
                    {
                        bool insideBox = BattleDefinitions.IsInsideDeployBox(dropWorld.x, dropWorld.z);
                        hud.Submit(new BattleCommand
                        {
                            Kind = CommandKind.Deploy,
                            UnitId = unit,
                            UseWorldPos = true,
                            TargetX = dropWorld.x,
                            TargetZ = dropWorld.z
                        });
                        if (app.LastResult.Accepted)
                        {
                            view.DragOverlay?.EndDragSuccess();
                            hud.Select(unit);
                        }
                        else
                        {
                            if (!troop.Deployed)
                                view.DragOverlay?.BounceBackToSlot(troop, point, dragOrigin, pauseOnLand: insideBox);
                            else
                                view.DragOverlay?.CancelDrag();
                        }
                        return;
                    }

                    hud.ShowTip("拖动超出白色布阵范围，放置失败", isError: true);
                    if (!troop.Deployed)
                        view.DragOverlay?.BounceBackToSlot(troop, point, dragOrigin, pauseOnLand: false);
                    else
                        view.DragOverlay?.CancelDrag();
                    return;
                }
                view.DragOverlay?.CancelDrag();
                return;
            }

            view.DragOverlay?.CancelDrag();
            var endHit = hud.Hit(point);
            if (button != null)
            {
                if (endHit?.Id == button) endHit.Click();
                return;
            }
            if (hud.BlocksWorld) return;
            var s = app.Session;
            if (s.Aim == AimKind.Focus)
            {
                int target = view.HitUnit(point, true);
                if (target > 0) hud.Submit(new BattleCommand { Kind = CommandKind.Focus, UnitId = target });
                return;
            }
            if (s.Aim == AimKind.Protect)
            {
                int lane = view.HitLane(point);
                if (lane >= 0) hud.Submit(new BattleCommand { Kind = CommandKind.Protect, Lane = lane });
                return;
            }
            if (s.Phase != RunPhase.Preparation) return;
            if (unit > 0) hud.Select(unit);
            else if (hud.Selected > 0 && view.ScreenToGround(point, out var tapWorld, requireViewport: true) && BattleDefinitions.IsInsideDeployBox(tapWorld.x, tapWorld.z))
            {
                var sel = s.Troops.FirstOrDefault(t => t.Id == hud.Selected);
                if (sel != null && sel.Deployed)
                    hud.Submit(new BattleCommand { Kind = CommandKind.Deploy, UnitId = hud.Selected, UseWorldPos = true, TargetX = tapWorld.x, TargetZ = tapWorld.z });
                else
                    hud.ClearSelection();
            }
            else hud.ClearSelection();
        }

        public void Cancel()
        {
            ClearHoldState(keepGhost: false);
            view?.SetDragZoneVisual(false, Vector3.zero, false, false);
        }

        private void ClearHoldState(bool keepGhost)
        {
            if (pressedVisual != null) pressedVisual.localScale = Vector3.one;
            pressedVisual = null;
            held = dragged = ghostStarted = false;
            heldUnit = 0;
            heldButton = null;
            finger = -1;
            if (!keepGhost) view?.DragOverlay?.CancelDrag();
        }
    }
}
