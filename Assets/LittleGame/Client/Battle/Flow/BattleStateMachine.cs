using System.Collections.Generic;
using LittleGame.Combat;

namespace LittleGame.Client.Battle
{
    /// <summary>战斗主流程阶段状态机（对齐 Demo BattleStateMachine）。</summary>
    public sealed class BattleStateMachine
    {
        private readonly Dictionary<RunPhase, BattlePhaseBase> states = new Dictionary<RunPhase, BattlePhaseBase>();
        private BattlePhaseBase current;

        public RunPhase CurrentPhase => current != null ? current.Phase : RunPhase.Preparation;

        public void Init(BattleModule module)
        {
            Register(new BattlePhasePreparation(), module);
            Register(new BattlePhaseFighting(), module);
            Register(new BattlePhaseWaveResult(), module);
            Register(new BattlePhaseReward(), module);
            Register(new BattlePhaseWon(), module);
            Register(new BattlePhaseLost(), module);
        }

        private void Register(BattlePhaseBase state, BattleModule module)
        {
            state.Init(module, this);
            states[state.Phase] = state;
        }

        public void SyncPhase(RunPhase next)
        {
            if (current != null && current.Phase == next)
            {
                return;
            }

            if (!states.TryGetValue(next, out var nextState))
            {
                return;
            }

            current?.OnExit();
            current = nextState;
            current.OnEnter();
            MVC.SendNotification(BattleNoti.BATTLE_PHASE_CHANGED, next);
        }

        public void Tick(float deltaTime)
        {
            current?.OnUpdate(deltaTime);
        }

        public void Clear()
        {
            current?.OnExit();
            current = null;
            states.Clear();
        }
    }
}
