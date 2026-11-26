using LittleGame.Combat;

namespace LittleGame.Client.Battle
{
    /// <summary>战斗阶段状态基类（对齐 Demo BattlePhaseBase）。</summary>
    public abstract class BattlePhaseBase
    {
        protected BattleModule Module { get; private set; }
        protected BattleStateMachine Machine { get; private set; }
        public abstract RunPhase Phase { get; }

        public void Init(BattleModule module, BattleStateMachine machine)
        {
            Module = module;
            Machine = machine;
        }

        public virtual void OnEnter() { }
        public virtual void OnUpdate(float deltaTime) { }
        public virtual void OnExit() { }
    }

    public sealed class BattlePhasePreparation : BattlePhaseBase
    {
        public override RunPhase Phase => RunPhase.Preparation;
    }

    public sealed class BattlePhaseFighting : BattlePhaseBase
    {
        public override RunPhase Phase => RunPhase.Fighting;
    }

    public sealed class BattlePhaseWaveResult : BattlePhaseBase
    {
        public override RunPhase Phase => RunPhase.WaveResult;
    }

    public sealed class BattlePhaseReward : BattlePhaseBase
    {
        public override RunPhase Phase => RunPhase.Reward;
    }

    public sealed class BattlePhaseWon : BattlePhaseBase
    {
        public override RunPhase Phase => RunPhase.Won;
    }

    public sealed class BattlePhaseLost : BattlePhaseBase
    {
        public override RunPhase Phase => RunPhase.Lost;
    }
}
