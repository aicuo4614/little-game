using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>死亡状态（对齐 Demo BattleUnitDieState）：播放一次 3D Death 动画后标记完成并隐藏模型。</summary>
    public sealed class BattleUnitDieState : BattleUnitStateBase
    {
        private float elapsed;
        private float duration;

        public bool IsComplete { get; private set; }

        public override void OnEnter()
        {
            elapsed = 0f;
            IsComplete = false;
            unit.StatusControl.SetStatusActive(false);
            unit.AnimatorControl.SetFrozen(false);
            duration = Mathf.Max(0.35f, unit.AnimatorControl.PlayDeath());
        }

        public override void OnUpdate(float deltaTime)
        {
            if (IsComplete)
            {
                return;
            }

            elapsed += deltaTime;
            if (elapsed >= duration)
            {
                IsComplete = true;
                unit.ModelControl.SetVisible(false);
            }
        }

        public override void RefreshPresentation()
        {
            unit.ModelControl.SetVisible(!IsComplete);
        }

        public override void OnExit()
        {
            IsComplete = false;
            unit.ModelControl.SetVisible(true);
        }
    }
}
