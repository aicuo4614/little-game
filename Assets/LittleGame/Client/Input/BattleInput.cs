using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client
{
    /// <summary>鼠标、触屏和MCP pointer完全共用此入口，包括取消和拖拽判定。</summary>
    public sealed class BattleInput : MonoBehaviour
    {
        private BattleApplication app;
        private BattleSceneView view;
        private BattleHud hud;
        private Vector2 start;
        private bool held, dragged;
        private int heldUnit, finger = -1;
        private string heldButton;
        private RectTransform pressedVisual;
        public void Bind(BattleApplication application, BattleSceneView scene, BattleHud ui) { app = application; view = scene; hud = ui; }
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
                Cancel(); held = true; start = point; var hit = hud.Hit(point); heldButton = hit?.Id; heldUnit = hit?.UnitId ?? 0;
                pressedVisual = hit?.Rect; if (pressedVisual != null) pressedVisual.localScale = Vector3.one * .96f;
                if (hit == null && !hud.BlocksWorld && app.Session.Phase == RunPhase.Preparation) heldUnit = view.HitUnit(point, false);
                return;
            }
            if (!held) return;
            float threshold = Mathf.Max(8, Screen.width * .015f);
            if (Vector2.Distance(point, start) > threshold) dragged = true;
            if (phase == "move") return;
            if (phase != "end") return;
            var button = heldButton; int unit = heldUnit; bool wasDragged = dragged; Cancel();
            if (wasDragged)
            {
                if (unit > 0 && !hud.BlocksWorld && app.Session.Phase == RunPhase.Preparation && view.HitCell(point, out int lane, out int row))
                { hud.Submit(new BattleCommand { Kind = CommandKind.Deploy, UnitId = unit, Lane = lane, Row = row }); hud.Select(unit); }
                return;
            }
            var endHit = hud.Hit(point);
            if (button != null) { if (endHit?.Id == button) endHit.Click(); return; }
            if (hud.BlocksWorld) return;
            var s = app.Session;
            if (s.Aim == AimKind.Focus)
            { int target = view.HitUnit(point, true); if (target > 0) hud.Submit(new BattleCommand { Kind = CommandKind.Focus, UnitId = target }); return; }
            if (s.Aim == AimKind.Protect)
            { int lane = view.HitLane(point); if (lane >= 0) hud.Submit(new BattleCommand { Kind = CommandKind.Protect, Lane = lane }); return; }
            if (s.Phase != RunPhase.Preparation) return;
            if (unit > 0) hud.Select(unit);
            else if (hud.Selected > 0 && view.HitCell(point, out int l, out int r)) hud.Submit(new BattleCommand { Kind = CommandKind.Deploy, UnitId = hud.Selected, Lane = l, Row = r });
            else hud.ClearSelection();
        }
        public void Cancel() { if (pressedVisual != null) pressedVisual.localScale = Vector3.one; pressedVisual = null; held = dragged = false; heldUnit = 0; heldButton = null; finger = -1; }
    }
}
