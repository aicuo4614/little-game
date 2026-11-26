using System.Linq;
using LittleGame.Client.Battle;
using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client
{
    /// <summary>
    /// 鼠标、触屏与 MCP pointer 共用输入入口：
    /// 1. 按下(tap)命中场上单位立即选中（只显示脚底光环，不弹出任何按钮面板）；
    /// 2. 拖拽单位时，超出白色布阵框不再变红拒绝，而是夹紧到可放置区域离手最近的点 A 并在 A 点显示半透明剪影预览，松手落位至 A；
    /// 3. 拖拽场上单位到候补军团区域松手，直接召回至候补；
    /// 4. 双击候补军团卡片自动寻找白色布阵框内最合适的空位上阵（与拖拽复用同一套 Deploy 规则）。
    /// </summary>
    public sealed class BattleInput : MonoBehaviour
    {
        private const float DoubleTapWindow = 0.42f;
        private const float CameraRotateDegreesPerScreenWidth = 115f;

        private BattleApplication app;
        private BattleView view;
        private BattleHud hud;
        private Vector2 start;
        private bool held, dragged, ghostStarted;
        private int heldUnit, finger = -1;
        private string heldButton;
        private RectTransform pressedVisual;

        // 双击候补卡片状态
        private int lastReserveTapUnitId;
        private float lastReserveTapTime = -10f;

        public void Bind(BattleApplication application, BattleView scene, BattleHud ui)
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
                {
                    heldUnit = view.HitUnit(point, false);
                    // Tap 即选中场上单位，无需等 click 松手
                    if (heldUnit > 0) hud.Select(heldUnit);
                }
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

                        bool overReserve = troop.Deployed && hud.IsPointInReserveArea(point);
                        hud.SetReserveDropHighlight(overReserve);

                        if (overReserve)
                        {
                            view.SetDragZoneVisual(true, Vector3.zero, showSpot: false, isReplace: false);
                            view.DragOverlay?.UpdateLandingPreview(false, Vector3.zero, false);
                        }
                        else if (view.ScreenToGround(point, out var ground, requireViewport: false))
                        {
                            // 超出白框外不再变红，夹紧到可放置区域离手最近的点 A，并在 A 点显示半透明剪影角色
                            BattleDefinitions.ClampToDeployBox(ground.x, ground.z, out float cx, out float cz);
                            Vector3 clampedPos = new Vector3(cx, 0.12f, cz);
                            bool occupied = app.Session.Troops.Any(t =>
                                t.Id != troop.Id && t.Deployed &&
                                Vector2.Distance(new Vector2(t.HomeX, t.HomeZ), new Vector2(cx, cz)) <= 0.78f);
                            view.SetDragZoneVisual(true, clampedPos, showSpot: true, isReplace: occupied);
                            view.DragOverlay?.UpdateLandingPreview(true, clampedPos, occupied);
                        }
                        else
                        {
                            view.SetDragZoneVisual(true, Vector3.zero, showSpot: false, isReplace: false);
                            view.DragOverlay?.UpdateLandingPreview(false, Vector3.zero, false);
                        }
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
            hud.SetReserveDropHighlight(false);
            view.SetDragZoneVisual(false, Vector3.zero, false, false);

            if (wasDragged)
            {
                var troop = app.Session.Troops.FirstOrDefault(t => t.Id == unit);
                if (unit > 0 && troop != null && !hud.BlocksWorld && app.Session.Phase == RunPhase.Preparation)
                {
                    // 1. 场上单位拖到候补区松手 -> 直接召回候补
                    if (troop.Deployed && hud.IsPointInReserveArea(point))
                    {
                        hud.Submit(new BattleCommand { Kind = CommandKind.Recall, UnitId = unit });
                        if (app.LastResult.Accepted)
                        {
                            view.DragOverlay?.EndDragSuccess();
                            hud.ClearSelection();
                        }
                        else
                        {
                            view.DragOverlay?.CancelDrag();
                        }
                        return;
                    }

                    // 2. 其他任意位置松手 -> 夹紧到可放置区域离手最近的点 A 落位
                    if (view.ScreenToGround(point, out var dropWorld, requireViewport: false))
                    {
                        BattleDefinitions.ClampToDeployBox(dropWorld.x, dropWorld.z, out float cx, out float cz);
                        hud.Submit(new BattleCommand
                        {
                            Kind = CommandKind.Deploy,
                            UnitId = unit,
                            UseWorldPos = true,
                            TargetX = cx,
                            TargetZ = cz
                        });
                        if (app.LastResult.Accepted)
                        {
                            view.DragOverlay?.EndDragSuccess();
                            hud.Select(unit);
                        }
                        else
                        {
                            if (!troop.Deployed)
                                view.DragOverlay?.BounceBackToSlot(troop, point, dragOrigin, pauseOnLand: true);
                            else
                                view.DragOverlay?.CancelDrag();
                        }
                        return;
                    }
                }
                view.DragOverlay?.CancelDrag();
                return;
            }

            view.DragOverlay?.CancelDrag();
            var endHit = hud.Hit(point);
            if (button != null)
            {
                if (endHit?.Id == button)
                {
                    // 双击候补卡片自动寻找合适位置上场（与拖拽复用同一套 Deploy 规则）
                    if (button.StartsWith("unit_") && unit > 0 && app.Session.Phase == RunPhase.Preparation && !hud.BlocksWorld)
                    {
                        var reserveTroop = app.Session.Troops.FirstOrDefault(t => t.Id == unit && !t.Deployed);
                        if (reserveTroop != null)
                        {
                            float now = Time.unscaledTime;
                            if (lastReserveTapUnitId == unit && (now - lastReserveTapTime) <= DoubleTapWindow)
                            {
                                lastReserveTapUnitId = 0;
                                lastReserveTapTime = -10f;
                                TryAutoDeployReserveTroop(reserveTroop);
                                return;
                            }
                            lastReserveTapUnitId = unit;
                            lastReserveTapTime = now;
                        }
                    }
                    else
                    {
                        lastReserveTapUnitId = 0;
                    }
                    endHit.Click();
                }
                return;
            }

            lastReserveTapUnitId = 0;
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
            if (unit > 0)
            {
                hud.Select(unit);
            }
            else if (hud.Selected > 0 && view.ScreenToGround(point, out var tapWorld, requireViewport: true))
            {
                // 点白色布阵框外空地取消选中；点框内空地仍可移动已选中单位
                if (!BattleDefinitions.IsInsideDeployBox(tapWorld.x, tapWorld.z))
                {
                    hud.ClearSelection();
                }
                else
                {
                    var sel = s.Troops.FirstOrDefault(t => t.Id == hud.Selected);
                    if (sel != null && sel.Deployed)
                    {
                        hud.Submit(new BattleCommand { Kind = CommandKind.Deploy, UnitId = hud.Selected, UseWorldPos = true, TargetX = tapWorld.x, TargetZ = tapWorld.z });
                    }
                    else
                    {
                        hud.ClearSelection();
                    }
                }
            }
            else
            {
                hud.ClearSelection();
            }
        }

        private void TryAutoDeployReserveTroop(Troop reserveTroop)
        {
            if (!app.Session.FindAutoDeployPosition(reserveTroop, out float ax, out float az))
            {
                hud.ShowTip("未找到合适的部署位置", isError: true);
                return;
            }
            hud.Submit(new BattleCommand
            {
                Kind = CommandKind.Deploy,
                UnitId = reserveTroop.Id,
                UseWorldPos = true,
                TargetX = ax,
                TargetZ = az
            });
            if (app.LastResult.Accepted)
                hud.Select(reserveTroop.Id);
        }

        public void Cancel()
        {
            ClearHoldState(keepGhost: false);
            hud?.SetReserveDropHighlight(false);
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
