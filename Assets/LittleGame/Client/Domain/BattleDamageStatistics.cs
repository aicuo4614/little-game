using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    public sealed class HeroDamageRecord
    {
        public string HeroId;
        public int Star;
        public float WaveDamage, TotalDamage;
    }

    public sealed partial class BattleSession
    {
        private readonly List<HeroDamageRecord> damageStatistics = new List<HeroDamageRecord>();
        public IReadOnlyList<HeroDamageRecord> DamageStatistics => damageStatistics;

        private void RecordDamage(BattleCombatant source, float amount)
        {
            if (!(source is Troop troop) || amount <= 0f) return;
            var record = damageStatistics.FirstOrDefault(r => r.HeroId == troop.Identity);
            if (record == null)
            {
                record = new HeroDamageRecord { HeroId = troop.Identity };
                damageStatistics.Add(record);
            }
            record.Star = System.Math.Max(record.Star, troop.Star);
            record.WaveDamage += amount;
            record.TotalDamage += amount;
        }
    }
}
