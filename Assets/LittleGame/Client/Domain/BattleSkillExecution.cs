using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    /// <summary>编辑器导出的纯规则数据；时序来自同一份时间轴，不依赖 Unity 动画回调。</summary>
    public sealed class BattleSkillDefinition
    {
        public string UnitKind, Trigger, Branch = "", TimelineId;
        public int MinStar = 1;
        public float LaunchAt, ImpactAt, RecoveryEnd;
        public float ProjectileSpeed, ProjectileDelay, ProjectileImpactLag, ProjectileMinFlight = .15f;
        public float ProjectileSourceX, ProjectileSourceY, ProjectileSourceZ;
        public Dictionary<string, float[]> ProjectileTargetPoints = new Dictionary<string, float[]>();
        public bool InterruptOnControl = true;
        public bool TrackTarget = true;
        public BattleSkillDefinition Copy() => (BattleSkillDefinition)MemberwiseClone();
        /// <summary>由已导出的挂点和实际施法距离计算到达时间，规则层不依赖 Unity 动画回调。</summary>
        public float ResolveImpact(float dx, float dz, float sourceYaw, float targetYaw,
            float sourceScale, float targetScale, string targetKind)
        {
            if (ProjectileSpeed <= 0f) return ImpactAt;
            var point = ProjectileTargetPoints.TryGetValue(targetKind, out var p) ? p : new[] { 0f, .8f, 0f };
            double sr = sourceYaw * Math.PI / 180.0, tr = targetYaw * Math.PI / 180.0;
            double x = dx + (point[0] * Math.Cos(tr) + point[2] * Math.Sin(tr)) * targetScale
                - (ProjectileSourceX * Math.Cos(sr) + ProjectileSourceZ * Math.Sin(sr)) * sourceScale;
            double z = dz + (-point[0] * Math.Sin(tr) + point[2] * Math.Cos(tr)) * targetScale
                - (-ProjectileSourceX * Math.Sin(sr) + ProjectileSourceZ * Math.Cos(sr)) * sourceScale;
            double y = point[1] * targetScale - ProjectileSourceY * sourceScale;
            return LaunchAt + ProjectileDelay + Math.Max(ProjectileMinFlight,
                (float)Math.Sqrt(x*x + y*y + z*z) / ProjectileSpeed) + ProjectileImpactLag;
        }
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(UnitKind) || string.IsNullOrWhiteSpace(Trigger) ||
                !Finite(LaunchAt) || !Finite(ImpactAt) || !Finite(RecoveryEnd) ||
                LaunchAt < 0 || ImpactAt < LaunchAt || RecoveryEnd < ImpactAt)
                throw new ArgumentException("技能必须满足 0 <= 发射 <= 命中 <= 动作结束，且时间必须有限");
            if (!Finite(ProjectileSpeed) || ProjectileSpeed < 0 || !Finite(ProjectileDelay) || ProjectileDelay < 0
                || !Finite(ProjectileMinFlight) || ProjectileMinFlight <= 0 || !Finite(ProjectileImpactLag) || ProjectileImpactLag < 0
                || !Finite(ProjectileSourceX) || !Finite(ProjectileSourceY) || !Finite(ProjectileSourceZ)
                || ProjectileTargetPoints == null || ProjectileTargetPoints.Any(p => p.Value == null || p.Value.Length != 3 || p.Value.Any(v => !Finite(v))))
                throw new ArgumentException("弹道速度、延迟和挂点必须为有效有限数值");
        }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    public enum BattleSkillPhase { Windup, Flying, Recovery, Completed, Cancelled }
    public sealed class BattleSkillInstance
    {
        public int Id, Source, Target, Lane;
        public bool EnemySource;
        public string Trigger, TimelineId, CancelReason;
        public float StartedAt, LaunchAt, ImpactAt, EndAt, CasterX, CasterZ, TargetX, TargetZ, FacingYaw;
        public BattleSkillPhase Phase;
        public bool Launched, Impacted;
        internal BattleSkillDefinition Definition;
    }

    public sealed partial class BattleSession
    {
        private readonly List<BattleSkillDefinition> skillDefinitions = new List<BattleSkillDefinition>();
        private readonly List<BattleSkillInstance> skillInstances = new List<BattleSkillInstance>();
        private int nextSkillId;
        private BattleSkillInstance resolvingSkill;
        public IReadOnlyList<BattleSkillInstance> SkillInstances => skillInstances;

        public void ConfigureSkills(IEnumerable<BattleSkillDefinition> definitions)
        {
            var copies = definitions.Select(d => d.Copy()).ToArray();
            foreach (var d in copies) d.Validate();
            skillDefinitions.Clear(); skillDefinitions.AddRange(copies);
            // 已释放实例持有独立定义；热重载只影响下一次释放。
        }

        private BattleSkillDefinition FindSkill(string kind, string trigger, string branch = "", int star = 1)
        {
            var found = skillDefinitions.Where(d => (d.UnitKind == kind || d.UnitKind == "*") && d.Trigger == trigger &&
                (string.IsNullOrEmpty(d.Branch) || d.Branch == branch) && star >= d.MinStar)
                .OrderByDescending(d => (d.UnitKind == "*" ? 0 : 100) + (string.IsNullOrEmpty(d.Branch) ? 0 : 50) + d.MinStar * 10).FirstOrDefault();
            return found ?? new BattleSkillDefinition { UnitKind = kind, Trigger = trigger, LaunchAt = .1f, ImpactAt = .2f, RecoveryEnd = .4f };
        }

        private bool SkillBusy(int source) => skillInstances.Any(a => a.Source == source && a.Phase != BattleSkillPhase.Cancelled && a.Phase != BattleSkillPhase.Completed);
        private void BeginSkill(int source, int target, int lane, bool enemy, BattleSkillDefinition definition)
        {
            var troop = troops.Find(t => t.Id == source);
            var foe = enemies.Find(e => e.Id == source);
            var targetTroop = troops.Find(t => t.Id == target);
            var targetEnemy = enemies.Find(e => e.Id == target);
            var a = new BattleSkillInstance {
                Id = ++nextSkillId, Source = source, Target = target, Lane = lane, EnemySource = enemy,
                Trigger = definition.Trigger, TimelineId = definition.TimelineId, Definition = definition.Copy(),
                StartedAt = BattleTime, LaunchAt = BattleTime + definition.LaunchAt,
                ImpactAt = BattleTime + definition.ImpactAt, EndAt = BattleTime + definition.RecoveryEnd,
                CasterX = enemy ? foe.X : troop.X, CasterZ = enemy ? foe.Z : troop.Z,
                FacingYaw = enemy ? foe.FacingYaw : troop.FacingYaw,
                TargetX = targetEnemy != null ? targetEnemy.X : targetTroop.X,
                TargetZ = targetEnemy != null ? targetEnemy.Z : targetTroop.Z,
                Phase = BattleSkillPhase.Windup
            };
            if (definition.ProjectileSpeed > 0f)
            {
                float sourceScale = !enemy && troop.Star > 1 ? (troop.Star == 2 ? 1.08f : 1.18f) : 1f;
                float targetScale = targetTroop != null && targetTroop.Star > 1 ? (targetTroop.Star == 2 ? 1.08f : 1.18f) : 1f;
                float impact = definition.ResolveImpact(a.TargetX - a.CasterX, a.TargetZ - a.CasterZ, a.FacingYaw,
                    targetEnemy != null ? targetEnemy.FacingYaw : targetTroop.FacingYaw, sourceScale, targetScale,
                    targetEnemy != null ? targetEnemy.Kind.ToString() : targetTroop.Kind.ToString());
                a.ImpactAt = BattleTime + impact;
                a.EndAt = BattleTime + Math.Max(definition.RecoveryEnd, impact + .08f);
            }
            skillInstances.Add(a);
            EmitSkillEvent("cast_started", a);
        }

        private void EmitSkillEvent(string type, BattleSkillInstance a, string reason = null)
        {
            var previous = resolvingSkill; resolvingSkill = a;
            Emit(type, reason ?? a.Trigger, a.Source, a.Target, a.Lane);
            resolvingSkill = previous;
        }

        private void StepSkills()
        {
            foreach (var a in skillInstances.ToArray())
            {
                if (a.Phase == BattleSkillPhase.Cancelled || a.Phase == BattleSkillPhase.Completed) continue;
                var troop = troops.Find(t => t.Id == a.Source);
                var enemy = enemies.Find(e => e.Id == a.Source);
                bool alive = a.EnemySource ? enemy != null && enemy.Alive : troop != null && troop.Alive;
                string cancel = !alive ? "caster_dead" : a.EnemySource && enemy.FrozenUntil > BattleTime && a.Definition.InterruptOnControl ? "controlled" : null;
                var targetEnemy = enemies.Find(e => e.Id == a.Target);
                var targetTroop = troops.Find(t => t.Id == a.Target);
                bool targetAlive = a.Trigger == "EnemyAttack" ? targetTroop != null && targetTroop.Alive : targetEnemy != null && targetEnemy.Alive;
                if (!a.Impacted && !targetAlive) cancel = "target_dead";
                if (cancel != null)
                {
                    a.Phase = BattleSkillPhase.Cancelled; a.CancelReason = cancel;
                    EmitSkillEvent("cast_cancelled", a, cancel); continue;
                }
                if (!a.Launched && BattleTime + .00001f >= a.LaunchAt)
                {
                    a.Launched = true; a.Phase = BattleSkillPhase.Flying;
                    EmitSkillEvent("cast_launched", a);
                }
                if (!a.Impacted && BattleTime + .00001f >= a.ImpactAt)
                {
                    a.Impacted = true; a.Phase = BattleSkillPhase.Recovery;
                    resolvingSkill = a;
                    if (a.Trigger == "EnemyAttack")
                    {
                        float reach = Math.Max(enemy.Boss ? 1.4f : .85f, BattleDefinitions.BodyRadius(enemy.Kind) + BattleDefinitions.BodyRadius(targetTroop.Kind) + .04f);
                        if (Distance(enemy.X, enemy.Z, targetTroop.X, targetTroop.Z) <= reach + .15f)
                            Hurt(enemy, targetTroop, BattleDefinitions.EnemyAttack(enemy.Kind, Wave));
                    }
                    else if (a.Trigger == "Heal")
                    {
                        float amount = Math.Min(targetEnemy.MaxHp - targetEnemy.Hp, BattleDefinitions.EnemyHealAmount(Wave));
                        targetEnemy.Hp += amount;
                        Emit("heal", "治疗师为敌人回血", a.Source, a.Target, a.Lane, amount);
                    }
                    else if (a.Trigger == "Skill") Skill(troop, targetEnemy);
                    else if (troop.Kind != TroopKind.Shield || BattleDefinitions.CanReach(troop, targetEnemy))
                        Damage(troop, targetEnemy, BattleDefinitions.Attack(troop), troop.Kind == TroopKind.Storm || troop.Kind == TroopKind.Frost, false);
                    EmitSkillEvent("cast_impact", a);
                    resolvingSkill = null;
                }
                if (BattleTime + .00001f >= a.EndAt)
                {
                    a.Phase = BattleSkillPhase.Completed; EmitSkillEvent("cast_completed", a);
                }
            }
            // 保留短期结果供 MCP 观察，活动实例永远保留。
            skillInstances.RemoveAll(a => (a.Phase == BattleSkillPhase.Completed || a.Phase == BattleSkillPhase.Cancelled) && BattleTime - a.EndAt > 2f);
        }
    }
}
