using System.Collections.Generic;

namespace LittleGame.Combat
{
    /// <summary>导航数据契约。规则层只发出意图并接收实际运动，不引用Unity。</summary>
    public static class BattleNavigation
    {
        public struct AgentInput
        {
            public int Id, TargetId;
            public bool IsTroop, HoldPosition, Airborne;
            public float X, Z, Radius, MaxSpeed, TargetX, TargetZ, TargetRadius, AttackReach;
            public BattleNavigationState State;
        }
        public struct AgentOutput
        {
            public int Id;
            public bool IsTroop, Waiting;
            public float NewX, NewZ, NewVx, NewVz;
        }
    }

    public interface IBattleNavigation
    {
        string Backend { get; }
        void Reset();
        void Step(IList<BattleNavigation.AgentInput> inputs, float dt, List<BattleNavigation.AgentOutput> outputs);
    }
}
