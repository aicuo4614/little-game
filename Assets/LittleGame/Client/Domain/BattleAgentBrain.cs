using System;
using System.Collections.Generic;

namespace LittleGame.Combat
{
    public enum CombatAiState { Idle, Pursuing, Engaging, Controlled, Dead, Waiting }

    /// <summary>单步AI意图。状态机只作决策，不移动实体、不结算伤害、不播放动画。</summary>
    internal struct CombatAiIntent
    {
        public CombatAiState State;
        public int TargetId;
        public float LookX, LookZ;
        public float TargetX, TargetZ, TargetRadius, AttackReach;
        public bool CanAttack;
        public bool HoldPosition => State != CombatAiState.Pursuing;
    }

    /// <summary>
    /// 确定性有限状态机：感知选目标 → 状态转移 → 输出追击/交战意图。
    /// 移动由战斗导航执行，攻击由BattleSimulation校验朝向、冷却后结算。
    /// 不引用Unity、表现层或MCP；玩家与工具运行同一套AI。
    /// </summary>
    internal static class BattleAgentBrain
    {
        private const float RetargetAdvantage = .65f;

        internal static CombatAiState Transition(CombatAiState current, bool alive, bool controlled,
            bool hasTarget, float distance, float attackReach, float arrivalMargin)
        {
            if (!alive) return CombatAiState.Dead;
            if (controlled) return CombatAiState.Controlled;
            if (!hasTarget) return CombatAiState.Idle;
            // 追击先进入射程内侧再站定；交战越过射程外沿才恢复追击，避免临界来回切换。
            switch (current)
            {
                case CombatAiState.Engaging:
                    return distance <= attackReach ? CombatAiState.Engaging : CombatAiState.Pursuing;
                default:
                    return distance <= attackReach - arrivalMargin ? CombatAiState.Engaging : CombatAiState.Pursuing;
            }
        }

        public static CombatAiIntent PlanTroop(Troop self, IReadOnlyList<Enemy> opponents, int focusId, float dt) =>
            Plan(self, opponents, focusId, 0f);

        public static CombatAiIntent PlanEnemy(Enemy self, IReadOnlyList<Troop> opponents, float time, float speed, float dt) =>
            Plan(self, opponents, 0, time);

        public static CombatAiIntent Plan(BattleCombatant self, IReadOnlyList<BattleCombatant> opponents, int focusId, float time)
        {
            BattleCombatant current = null, nearest = null, reachable = null, focused = null;
            float nearestD = float.MaxValue, reachableD = float.MaxValue;
            foreach (var other in opponents)
            {
                if (!other.Deployed || !other.Alive || other.Side == self.Side) continue;
                float distance = Distance(self.X, self.Z, other.X, other.Z);
                if (other.Id == self.TargetId) current = other;
                if (distance < nearestD || (distance == nearestD && other.Id < nearest.Id))
                {
                    nearest = other;
                    nearestD = distance;
                }
                if (distance > BattleDefinitions.Reach(self, other)) continue;
                if (other.Id == focusId) focused = other;
                if (distance < reachableD || (distance == reachableD && other.Id < reachable.Id))
                {
                    reachable = other;
                    reachableD = distance;
                }
            }

            var target = focused ?? (current != null && BattleDefinitions.CanReach(self, current) ? current : reachable);
            if (target == null)
                target = current == null || nearestD + RetargetAdvantage < Distance(self.X, self.Z, current.X, current.Z) ? nearest : current;

            float targetX = target?.X ?? self.X;
            float targetZ = target?.Z ?? self.Z;
            float reach = target == null ? 0f : BattleDefinitions.Reach(self, target);
            float distanceToTarget = Distance(self.X, self.Z, targetX, targetZ);
            var state = Transition(self.AiState, self.Alive, !self.Status.CanMove(time), target != null, distanceToTarget, reach, .04f);
            return new CombatAiIntent
            {
                State = state, TargetId = target?.Id ?? 0, LookX = targetX - self.X, LookZ = targetZ - self.Z,
                TargetX = targetX, TargetZ = targetZ, TargetRadius = target?.Properties.Radius ?? 0f, AttackReach = reach,
                CanAttack = target != null && self.Status.CanAttack(time) && distanceToTarget <= reach
            };
        }

        private static float Distance(float ax, float az, float bx, float bz) =>
            (float)Math.Sqrt(BattleDefinitions.DistanceSquared(ax, az, bx, bz));
    }
}
