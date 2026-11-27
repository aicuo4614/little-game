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
        internal CombatSkill Skill;
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

        private BattleSkillDefinition FindSkill(string kind, string trigger, int star)
        {
            var found = skillDefinitions.Where(d => (d.UnitKind == kind || d.UnitKind == "*") && d.Trigger == trigger &&
                string.IsNullOrEmpty(d.Branch) && star >= d.MinStar)
                .OrderByDescending(d => (d.UnitKind == "*" ? 0 : 100) + d.MinStar).FirstOrDefault();
            return found ?? new BattleSkillDefinition { UnitKind = kind, Trigger = trigger, LaunchAt = .10f, ImpactAt = .25f, RecoveryEnd = .52f };
        }

        private bool SkillBusy(int source) => skillInstances.Any(a => a.Source == source && a.Phase != BattleSkillPhase.Cancelled && a.Phase != BattleSkillPhase.Completed);

        private void BeginSkill(BattleCombatant unit, BattleCombatant target, CombatSkill skill)
        {
            string kind = skill?.Timing;
            string trigger = "Skill";
            if (skill == null)
            {
                kind = unit.Definition.BasicTiming;
                trigger = unit.Side == BattleSide.Enemy ? "EnemyAttack" : "BasicAttack";
            }
            else if (skill.Effect == SkillEffect.Heal) trigger = "Heal";
            else if (skill.Effect == SkillEffect.Slam) trigger = "BossSlam";

            var timing = FindSkill(kind, trigger, unit.Star).Copy();
            int lane = BattleDefinitions.NearestLane(target.Z);
            if (skill?.Effect == SkillEffect.Slam)
            {
                timing.TimelineId = null;
                timing.LaunchAt = .10f;
                timing.ImpactAt = 1.5f;
                timing.RecoveryEnd = 2.1f;
                slamLane = lane;
                slamAt = BattleTime + timing.ImpactAt;
                Emit("boss_telegraph", "巨像蓄力横扫", unit.Id, 0, lane);
            }

            var cast = new BattleSkillInstance
            {
                Id = ++nextSkillId, Source = unit.Id, Target = target.Id, Lane = lane, EnemySource = unit.Side == BattleSide.Enemy,
                Trigger = trigger, TimelineId = timing.TimelineId, Definition = timing, Skill = skill,
                StartedAt = BattleTime, LaunchAt = BattleTime + timing.LaunchAt,
                ImpactAt = BattleTime + timing.ImpactAt, EndAt = BattleTime + timing.RecoveryEnd,
                CasterX = unit.X, CasterZ = unit.Z, FacingYaw = unit.FacingYaw,
                TargetX = target.X, TargetZ = target.Z, Phase = BattleSkillPhase.Windup
            };
            if (timing.ProjectileSpeed > 0f)
            {
                float sourceScale = unit.Star == 3 ? 1.18f : unit.Star == 2 ? 1.08f : 1f;
                float targetScale = target.Star == 3 ? 1.18f : target.Star == 2 ? 1.08f : 1f;
                string targetKind = target is Troop ally ? ally.Kind.ToString() : target.Definition.Id;
                float impact = timing.ResolveImpact(target.X - unit.X, target.Z - unit.Z, unit.FacingYaw,
                    target.FacingYaw, sourceScale, targetScale, targetKind);
                cast.ImpactAt = BattleTime + impact;
                cast.EndAt = BattleTime + Math.Max(timing.RecoveryEnd, impact + .08f);
            }
            skillInstances.Add(cast);
            EmitSkillEvent("cast_started", cast);
        }

        private void EmitSkillEvent(string type, BattleSkillInstance cast, string reason = null)
        {
            var previous = resolvingSkill;
            resolvingSkill = cast;
            Emit(type, reason ?? cast.Skill?.Name ?? cast.Trigger, cast.Source, cast.Target, cast.Lane);
            resolvingSkill = previous;
        }

        private void StepSkills()
        {
            foreach (var cast in skillInstances)
            {
                if (cast.Phase == BattleSkillPhase.Cancelled || cast.Phase == BattleSkillPhase.Completed) continue;
                var source = FindUnit(cast.Source);
                var target = FindUnit(cast.Target);
                bool slam = cast.Skill?.Effect == SkillEffect.Slam;
                string cancel = null;
                if (!cast.Impacted)
                {
                    if (source == null || !source.Alive) cancel = "caster_dead";
                    else if (!cast.Launched && cast.Definition.InterruptOnControl &&
                        !(cast.Skill == null ? source.Status.CanAttack(BattleTime) : source.Status.CanCast(BattleTime)))
                        cancel = "controlled";
                    else if (!slam && (target == null || !target.Alive)) cancel = "target_dead";
                }
                if (cancel != null)
                {
                    cast.Phase = BattleSkillPhase.Cancelled;
                    cast.CancelReason = cancel;
                    if (slam) { slamAt = -1f; slamLane = -1; }
                    EmitSkillEvent("cast_cancelled", cast, cancel);
                    continue;
                }
                if (!cast.Launched && BattleTime + .00001f >= cast.LaunchAt)
                {
                    cast.Launched = true;
                    cast.Phase = BattleSkillPhase.Flying;
                    EmitSkillEvent("cast_launched", cast);
                }
                if (!cast.Impacted && BattleTime + .00001f >= cast.ImpactAt)
                {
                    cast.Impacted = true;
                    cast.Phase = BattleSkillPhase.Recovery;
                    resolvingSkill = cast;
                    ResolveImpact(cast, source, target);
                    EmitSkillEvent("cast_impact", cast);
                    resolvingSkill = null;
                }
                if (BattleTime + .00001f >= cast.EndAt)
                {
                    cast.Phase = BattleSkillPhase.Completed;
                    EmitSkillEvent("cast_completed", cast);
                }
            }
            skillInstances.RemoveAll(a => (a.Phase == BattleSkillPhase.Completed || a.Phase == BattleSkillPhase.Cancelled) && BattleTime - a.EndAt > 2f);
        }

        private IEnumerable<BattleCombatant> FieldUnits() =>
            troops.Where(t => t.Deployed && t.Alive).Cast<BattleCombatant>().Concat(enemies.Where(e => e.Alive));

        private void ResolveImpact(BattleSkillInstance cast, BattleCombatant source, BattleCombatant target)
        {
            var skill = cast.Skill;
            float attack = source.Properties.Attack;
            if (skill == null)
            {
                if (source.Properties.AttackRange > 2.3f || BattleDefinitions.CanReach(source, target))
                    DealDamage(source, target, attack, source.Definition.Magic, basic: true);
                return;
            }

            Emit("skill", skill.Name, source.Id, target?.Id ?? 0, cast.Lane);
            float damage = attack * skill.Power * source.Properties.SkillMultiplier;
            bool magic = source.Definition.Magic || skill.Effect == SkillEffect.MagicStrike || skill.Effect == SkillEffect.MagicSplash || skill.Effect == SkillEffect.Freeze;
            switch (skill.Effect)
            {
                case SkillEffect.Heal:
                    float amount = Math.Min(target.MaxHp - target.Hp, damage);
                    target.Hp += amount;
                    Emit("heal", skill.Name, source.Id, target.Id, target.Lane, amount);
                    return;
                case SkillEffect.GuardStrike:
                    source.Buffs.Apply(UnitModifier.Shield, "skill/" + source.Id, source.MaxHp * skill.Secondary, BattleTime + skill.Duration);
                    break;
                case SkillEffect.TeamGuard:
                    foreach (var ally in FieldUnits().Where(u => u.Side == source.Side && Distance(u.X, u.Z, source.X, source.Z) <= 3.2f))
                        ally.Buffs.Apply(UnitModifier.Shield, "skill/" + source.Id, ally.MaxHp * skill.Secondary, BattleTime + skill.Duration);
                    break;
                case SkillEffect.Slam:
                    float z = BattleDefinitions.CellZ(cast.Lane);
                    foreach (var unit in FieldUnits().Where(u => u.Side != source.Side && Math.Abs(u.Z - z) <= skill.Secondary).ToArray())
                        DealDamage(source, unit, damage, magic);
                    Emit("boss_slam", skill.Name, source.Id, 0, cast.Lane);
                    slamAt = -1f; slamLane = -1;
                    return;
                case SkillEffect.MagicSplash:
                    float radius = SkillRadius(source, skill.Secondary);
                    zones.Add(new StormZone { Source = source.Id, Lane = target.Lane, X = target.X, Z = target.Z,
                        Remaining = 1.6f, UntilTick = .5f, Radius = radius, Damage = damage * .23f });
                    break;
            }

            if (source.Properties.AttackRange <= 2.3f && !BattleDefinitions.CanReach(source, target)) return;
            DealDamage(source, target, damage, magic);
            if (skill.Effect == SkillEffect.Freeze) ApplyFreeze(source, target, skill.Duration);
            if (skill.Secondary <= 0f || (skill.Effect != SkillEffect.Splash && skill.Effect != SkillEffect.MagicSplash && skill.Effect != SkillEffect.Freeze)) return;

            float area = SkillRadius(source, skill.Secondary);
            foreach (var other in FieldUnits().Where(u => u.Side != source.Side && u != target && Distance(u.X, u.Z, target.X, target.Z) <= area).ToArray())
            {
                DealDamage(source, other, damage * .68f, magic);
                if (skill.Effect == SkillEffect.Freeze) ApplyFreeze(source, other, skill.Duration);
            }
        }

        private float SkillRadius(BattleCombatant source, float radius) =>
            source.Side == BattleSide.Ally && source.Definition.Magic && rewards.Contains(RewardKind.WideStorm) ? radius * 1.35f : radius;

        private void ApplyFreeze(BattleCombatant source, BattleCombatant target, float duration)
        {
            if (!target.Alive) return;
            if (target.Boss)
            {
                target.Status.Apply(UnitStatus.Slowed, source.Id, BattleTime + duration);
                Emit("boss_control_resisted", "首领冻结转为减速", source.Id, target.Id, target.Lane);
                return;
            }
            if (source.Side == BattleSide.Ally && rewards.Contains(RewardKind.LongFreeze)) duration += .5f;
            target.Status.Apply(UnitStatus.Frozen, source.Id, BattleTime + duration);
            Emit("freeze", "冻结", source.Id, target.Id, target.Lane, duration);
        }
    }
}
