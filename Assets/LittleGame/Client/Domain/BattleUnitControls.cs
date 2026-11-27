using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    public enum UnitStatus { Stunned, Frozen, Silenced, Rooted, Slowed }

    public sealed class UnitStatusControl
    {
        private readonly Dictionary<(UnitStatus, int), float> effects = new Dictionary<(UnitStatus, int), float>();
        private readonly List<(UnitStatus, int)> expired = new List<(UnitStatus, int)>();

        public void Apply(UnitStatus status, int source, float until)
        {
            var key = (status, source);
            if (!effects.TryGetValue(key, out float current) || current < until) effects[key] = until;
        }

        public float Until(UnitStatus status)
        {
            float until = 0f;
            foreach (var effect in effects)
                if (effect.Key.Item1 == status && effect.Value > until) until = effect.Value;
            return until;
        }
        public bool Has(UnitStatus status, float time) => Until(status) > time;
        public bool CanAttack(float time) => !Has(UnitStatus.Stunned, time) && !Has(UnitStatus.Frozen, time);
        public bool CanCast(float time) => CanAttack(time) && !Has(UnitStatus.Silenced, time);
        public bool CanMove(float time) => CanAttack(time) && !Has(UnitStatus.Rooted, time);
        public bool CanJump(float time) => CanMove(time);
        public void Remove(UnitStatus status, int source) => effects.Remove((status, source));
        public void Clear() => effects.Clear();

        public void Tick(float time)
        {
            expired.Clear();
            foreach (var effect in effects)
                if (effect.Value <= time) expired.Add(effect.Key);
            for (int i = 0; i < expired.Count; i++) effects.Remove(expired[i]);
        }

        public object Snapshot() => effects.Select(e => new { status = e.Key.Item1.ToString(), source = e.Key.Item2, until = e.Value }).ToArray();
    }

    public sealed class UnitBuffControl
    {
        private sealed class Buff
        {
            public UnitModifier Stat;
            public string Source;
            public float Amount, Until;
        }

        private readonly List<Buff> effects = new List<Buff>();

        public void Apply(UnitModifier stat, string source, float amount, float until)
        {
            var existing = effects.Find(e => e.Stat == stat && e.Source == source);
            if (existing == null) effects.Add(new Buff { Stat = stat, Source = source, Amount = amount, Until = until });
            else { existing.Amount = amount; existing.Until = until; }
        }

        public float Total(UnitModifier stat)
        {
            float total = 0f;
            foreach (var effect in effects)
                if (effect.Stat == stat) total += effect.Amount;
            return total;
        }

        public float Absorb(float damage)
        {
            foreach (var effect in effects)
            {
                if (effect.Stat != UnitModifier.Shield) continue;
                float absorbed = Math.Min(effect.Amount, damage);
                effect.Amount -= absorbed;
                damage -= absorbed;
                if (damage <= 0f) break;
            }
            effects.RemoveAll(e => e.Stat == UnitModifier.Shield && e.Amount <= 0f);
            return damage;
        }

        public void RemoveSource(string source) => effects.RemoveAll(e => e.Source == source);
        public void Tick(float time) => effects.RemoveAll(e => e.Until <= time);
        public void Clear() => effects.Clear();
        public object Snapshot() => effects.Select(e => new { stat = e.Stat.ToString(), source = e.Source, amount = e.Amount, until = e.Until == float.MaxValue ? (float?)null : e.Until }).ToArray();
    }

    public sealed class UnitPropertyControl
    {
        private readonly BattleCombatant unit;
        public UnitPropertyControl(BattleCombatant unit) { this.unit = unit; }

        public float Health => unit.Definition.Hp * BattleDefinitions.StarScale(unit.Star) * (1f + unit.Buffs.Total(UnitModifier.Health));
        public float Attack => unit.Definition.Attack * BattleDefinitions.StarScale(unit.Star) * (1f + unit.Buffs.Total(UnitModifier.Attack) + unit.Buffs.Total(UnitModifier.LandingDamage));
        public float Armor => unit.Definition.Armor + unit.Buffs.Total(UnitModifier.Armor);
        public float MagicResist => unit.Definition.Resist;
        public float AttackInterval => 1f / (unit.Definition.AttacksPerSecond * (1f + unit.Buffs.Total(UnitModifier.Speed)));
        public float MoveSpeed => unit.Definition.Move;
        public float AttackRange => unit.Definition.Range;
        public float Radius => unit.Definition.Radius;
        public float TurnSpeed => unit.Definition.Turn;
        public float SkillMultiplier => 1f + unit.Buffs.Total(UnitModifier.SkillDamage);

        public object Snapshot() => new { health = Health, attack = Attack, armor = Armor, magicResist = MagicResist,
            attackSpeed = 1f / AttackInterval, moveSpeed = MoveSpeed, range = AttackRange };
    }

    public sealed class BattleBuffControl
    {
        private readonly List<SynergyDefinition> active = new List<SynergyDefinition>();
        private readonly List<SynergyDefinition> enemyActive = new List<SynergyDefinition>();
        public IReadOnlyList<SynergyDefinition> Active => active;
        public IReadOnlyList<SynergyDefinition> EnemyActive => enemyActive;

        public void Apply(IReadOnlyList<BattleCombatant> units, BattleSide side = BattleSide.Ally)
        {
            var effects = side == BattleSide.Ally ? active : enemyActive;
            effects.Clear();
            var field = units.Where(t => t.Deployed).GroupBy(t => t.Definition.Id).Select(g => g.First()).ToArray();
            var matches = BattleTables.Current.Synergies.Where(s => field.Count(t => s.Matches(t.Definition)) >= s.Count);
            foreach (var group in matches.GroupBy(s => (s.Race, s.Job)))
                effects.Add(group.OrderByDescending(s => s.Count).First());

            foreach (var troop in units)
            {
                troop.Buffs.RemoveSource("synergy");
                if (!troop.Deployed) continue;
                foreach (var synergy in effects)
                    if (synergy.Duration <= 0f && synergy.Matches(troop.Definition))
                        troop.Buffs.Apply(synergy.Stat, "synergy", synergy.Amount, float.MaxValue);
            }
        }

        public void OnLanding(BattleCombatant unit, float time)
        {
            var effects = unit.Side == BattleSide.Ally ? active : enemyActive;
            foreach (var synergy in effects)
                if (synergy.Duration > 0f && synergy.Matches(unit.Definition))
                    unit.Buffs.Apply(synergy.Stat, "landing", synergy.Amount, time + synergy.Duration);
        }
    }
}
