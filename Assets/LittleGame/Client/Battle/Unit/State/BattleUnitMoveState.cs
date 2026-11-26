using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>移动状态：循环播放 3D Animator 的 Walk 状态并按实际移速缩放步频。</summary>
    public sealed class BattleUnitMoveState : BattleUnitStateBase
    {
        public override void OnEnter()
        {
            RefreshPresentation();
        }

        public override void OnUpdate(float deltaTime)
        {
            float speedFactor = unit.AppliedMovementSpeed > 0.05f
                ? Mathf.Clamp(unit.AppliedMovementSpeed / 1.85f, 0.75f, 1.45f)
                : 1f;
            unit.AnimatorControl.SetWalkSpeed(speedFactor);
        }

        public override void RefreshPresentation()
        {
            float speedFactor = unit.AppliedMovementSpeed > 0.05f
                ? Mathf.Clamp(unit.AppliedMovementSpeed / 1.85f, 0.75f, 1.45f)
                : 1f;
            unit.AnimatorControl.PlayWalk(speedFactor);
        }
    }
}
