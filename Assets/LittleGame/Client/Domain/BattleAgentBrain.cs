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

        public static CombatAiIntent PlanTroop(Troop self, IReadOnlyList<Enemy> enemies, int focusId, float dt)
        {
            Enemy current = null, nearest = null, reachable = null, focused = null;
            float nearestD = float.MaxValue, reachableD = float.MaxValue;
            foreach (var e in enemies)
            {
                if (!e.Alive) continue;
                float d = Distance(self.X, self.Z, e.X, e.Z);
                if (e.Id == self.TargetId) current = e;
                if (d < nearestD || (d == nearestD && e.Id < nearest.Id)) { nearest = e; nearestD = d; }
                if (!BattleDefinitions.CanReach(self, e)) continue;
                if (e.Id == focusId) focused = e;
                if (d < reachableD || (d == reachableD && e.Id < reachable.Id)) { reachable = e; reachableD = d; }
            }
            Enemy target = focused;
            if (target == null && current != null && BattleDefinitions.CanReach(self, current)) target = current;
            if (target == null) target = reachable;
            if (target == null)
            {
                target = current;
                if (target == null || nearestD + RetargetAdvantage < Distance(self.X, self.Z, target.X, target.Z)) target = nearest;
            }
            if (target == null) return Plan(self.AiState, self.Alive, false, 0, self.X, self.Z, self.X, self.Z, 0, .06f);
            var intent = Plan(self.AiState, self.Alive, false, target.Id, self.X, self.Z, target.X, target.Z,
                BattleDefinitions.Range(self) + (target.Boss ? 1.25f : .15f), .06f);
            intent.TargetX = target.X; intent.TargetZ = target.Z;
            intent.TargetRadius = BattleDefinitions.BodyRadius(target.Kind);
            intent.AttackReach = BattleDefinitions.Range(self) + (target.Boss ? 1.25f : .15f);
            return intent;
        }

        public static CombatAiIntent PlanEnemy(Enemy self, IReadOnlyList<Troop> troops, float time, float speed, float dt)
        {
            Troop current = null, nearest = null;
            float nearestD = float.MaxValue;
            foreach (var t in troops)
            {
                if (!t.Deployed || !t.Alive) continue;
                if (t.Id == self.TargetId) current = t;
                float d = Distance(self.X, self.Z, t.X, t.Z);
                if (d < nearestD || (d == nearestD && t.Id < nearest.Id)) { nearest = t; nearestD = d; }
            }
            var target = current;
            if (target == null || nearestD + RetargetAdvantage < Distance(self.X, self.Z, target.X, target.Z)) target = nearest;
            float reach = target == null ? 0 : Math.Max(self.Boss ? 2.15f : 1.05f,
                BattleDefinitions.BodyRadius(self.Kind) + BattleDefinitions.BodyRadius(target.Kind) + .04f);
            var intent = Plan(self.AiState, self.Alive, self.FrozenUntil > time, target?.Id ?? 0,
                self.X, self.Z, target?.X ?? self.X, target?.Z ?? self.Z,
                reach, .02f);
            intent.TargetX = target?.X ?? self.X; intent.TargetZ = target?.Z ?? self.Z;
            intent.TargetRadius = target == null ? 0 : BattleDefinitions.BodyRadius(target.Kind);
            intent.AttackReach = reach;
            return intent;
        }

        private static CombatAiIntent Plan(CombatAiState current, bool alive, bool controlled, int targetId,
            float x, float z, float targetX, float targetZ, float reach, float arrivalMargin)
        {
            float distance = Distance(x, z, targetX, targetZ);
            var state = Transition(current, alive, controlled, targetId != 0, distance, reach, arrivalMargin);
            return new CombatAiIntent { State = state, TargetId = targetId,
                LookX = targetX - x, LookZ = targetZ - z,
                CanAttack = state == CombatAiState.Engaging && distance <= reach };
        }

        private static float Distance(float ax, float az, float bx, float bz)
        { float dx = ax - bx, dz = az - bz; return (float)Math.Sqrt(dx * dx + dz * dz); }
    }
}
