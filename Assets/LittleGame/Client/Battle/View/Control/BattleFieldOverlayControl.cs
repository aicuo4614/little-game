using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>战场布阵白框、雷池法阵、选中光标与场景压暗子系统。</summary>
    public sealed class BattleFieldOverlayControl : BattleControlBase
    {
        private BattleFieldOverlayView overlay;

        public override void Init(BattleView owner)
        {
            base.Init(owner);
            overlay = new GameObject("BattleFieldOverlay").AddComponent<BattleFieldOverlayView>();
            overlay.transform.SetParent(owner.WorldRoot, false);
            overlay.Initialize(owner.SceneControl.CombatSun, owner.CameraControl.Camera, owner.RingMesh);
        }

        public void Sync(BattleSession session, float unscaledDt)
        {
            overlay?.Sync(session, Owner.UnitsControl, unscaledDt);
        }

        public void SyncSelectionCursor(int selectedUnitId, bool visible)
        {
            overlay?.SyncSelectionCursor(selectedUnitId, visible, Owner.UnitsControl);
        }

        public void SetDragZoneVisual(bool dragging, Vector3 clampedPos, bool showSpot, bool isReplace)
        {
            overlay?.SetDragZoneVisual(dragging, clampedPos, showSpot, isReplace);
        }

        public void SetTargetSceneDarken(float strength)
        {
            overlay?.SetTargetSceneDarken(strength);
        }

        public void ResetDarkenImmediate()
        {
            SceneDarkenUtil.SetMultiplyColor(Color.white);
            overlay?.ResetDarkenImmediate();
        }

        public override void ReleaseResources()
        {
            ResetDarkenImmediate();
            if (overlay != null)
            {
                Object.Destroy(overlay.gameObject);
                overlay = null;
            }
        }
    }
}
