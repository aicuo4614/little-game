using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LittleGame.Combat
{
    public enum UnitRace { Human, Undead, Monster }
    public enum UnitJob { Warrior, Archer, Mage, Assassin }
    public enum UnitGrade { Hero, Normal, Elite, Boss }
    public enum SkillEffect { Strike, MagicStrike, Splash, MagicSplash, GuardStrike, TeamGuard, Freeze, Heal, Slam, ArmorBonus, SpeedBonus, ManaBonus }
    public enum UnitModifier { Attack, Health, Armor, Speed, SkillDamage, LandingDamage, ManaGain, Shield }
    public enum BattleTuning { BasicMana, DamageManaBase, DamageManaHealthRatio, DamageManaLimit, TwoStarScale, ThreeStarScale,
        AssassinDetection, AssassinJumpRange, AssassinJumpDuration, AssassinEarliestJump,
        RecruitGrowthWave, RecruitProfessionBias, RecruitHeroBias }

    public sealed class UnitDefinition
    {
        public string Id, Name, Role, BasicTiming;
        public UnitRace Race;
        public UnitJob Job;
        public UnitGrade Grade;
        public float Hp, Attack, Armor, Resist, AttacksPerSecond, Move, Range, Radius, Turn;
        public bool Magic;
        public CombatSkill[] Skills;
    }

    public sealed class CombatSkill
    {
        public string Id, Name, Timing;
        public SkillEffect Effect;
        public float Power, Secondary, Duration, Cooldown;
        public bool Passive => Effect == SkillEffect.ArmorBonus || Effect == SkillEffect.SpeedBonus || Effect == SkillEffect.ManaBonus;
    }

    public sealed class SynergyDefinition
    {
        public UnitRace? Race;
        public UnitJob? Job;
        public int Count;
        public UnitModifier Stat;
        public float Amount, Duration;

        public bool Matches(UnitDefinition unit) => Race.HasValue ? unit.Race == Race : unit.Job == Job;
    }

    public sealed class BattleTables
    {
        private sealed class SpawnRow
        {
            public int Wave, Batch, Star, Count, Lane;
            public float MinDelay, MaxDelay;
            public string Monster;
        }

        private readonly Dictionary<string, UnitDefinition> units;
        private readonly Dictionary<UnitJob, string[]> recruits;
        private readonly Dictionary<int, SpawnRow[]> waves;
        private readonly Dictionary<int, int> caps;
        private readonly HashSet<int> rewardWaves;
        private readonly HashSet<int> enemySynergyWaves;
        private readonly Dictionary<BattleTuning, float> tuning;
        public IReadOnlyList<SynergyDefinition> Synergies { get; }
        public int TotalWaves => caps.Count;
        public IEnumerable<UnitDefinition> Units => units.Values;

        private static BattleTables current;
        public static BattleTables Current
        {
            get
            {
#if !UNITY_5_3_OR_NEWER
                if (current == null) current = new BattleTables(ReadProjectFile);
#endif
                return current ?? throw new InvalidOperationException("尚未加载战斗配表");
            }
        }
        public static void Configure(Func<string, string> read) => current = new BattleTables(read);

        public BattleTables(Func<string, string> read)
        {
            tuning = Rows(read("tuning"), 2).ToDictionary(r => EnumValue<BattleTuning>(r[0]), r => Number(r[1]));
            if (tuning.Count != Enum.GetValues(typeof(BattleTuning)).Length ||
                tuning[BattleTuning.AssassinJumpDuration] <= 0 || tuning[BattleTuning.TwoStarScale] < 1 ||
                tuning[BattleTuning.ThreeStarScale] < tuning[BattleTuning.TwoStarScale] ||
                tuning[BattleTuning.RecruitProfessionBias] > 100 || tuning[BattleTuning.RecruitHeroBias] > 100)
                throw new InvalidDataException("战斗调校表缺值或范围无效");
            var skills = Rows(read("skills"), 8).Select(r => new CombatSkill
            {
                Id = r[0], Name = r[1], Effect = EnumValue<SkillEffect>(r[2]),
                Power = Number(r[3]), Secondary = Number(r[4]), Duration = Number(r[5]), Cooldown = Number(r[6]), Timing = r[7]
            }).ToDictionary(s => s.Id, StringComparer.Ordinal);

            units = Rows(read("units"), 20).Select(r => new UnitDefinition
            {
                Id = r[0], Name = r[1], Race = EnumValue<UnitRace>(r[2]), Job = EnumValue<UnitJob>(r[3]), Grade = EnumValue<UnitGrade>(r[4]),
                Role = r[5], Hp = Positive(r[6]), Attack = Positive(r[7]), Armor = Number(r[8]), Resist = Number(r[9]),
                AttacksPerSecond = Positive(r[10]), Move = Positive(r[11]), Range = Positive(r[12]), Radius = Positive(r[13]),
                Turn = Positive(r[14]), Magic = Integer(r[15], 0, 1) == 1,
                BasicTiming = r[16], Skills = r.Skip(17).Select(id => id.Length == 0 ? null : skills[id]).ToArray()
            }).ToDictionary(u => u.Id, StringComparer.Ordinal);
            if (units.Values.Any(u => u.Grade == UnitGrade.Hero && u.Skills.Any(s => s == null)))
                throw new InvalidDataException("英雄需要三个固定技能");

            recruits = Rows(read("recruits"), 2)
                .GroupBy(r => EnumValue<UnitJob>(r[0]))
                .ToDictionary(g => g.Key, g => g.Select(r => r[1]).ToArray());
            foreach (UnitJob job in Enum.GetValues(typeof(UnitJob)))
                if (!recruits.TryGetValue(job, out var pool) || pool.Distinct().Count() != pool.Length ||
                    pool.Any(id => Unit(id).Job != job || Unit(id).Grade != UnitGrade.Hero))
                    throw new InvalidDataException("招募候选不符合职业：" + job);

            Synergies = Rows(read("synergies"), 6).Select(r => new SynergyDefinition
            {
                Race = r[0].Length == 0 ? (UnitRace?)null : EnumValue<UnitRace>(r[0]),
                Job = r[1].Length == 0 ? (UnitJob?)null : EnumValue<UnitJob>(r[1]),
                Count = Integer(r[2], 1, 9), Stat = EnumValue<UnitModifier>(r[3]), Amount = Number(r[4]), Duration = Number(r[5])
            }).ToArray();
            if (Synergies.Any(s => s.Race.HasValue == s.Job.HasValue))
                throw new InvalidDataException("羁绊需且只需指定一种族或职业");

            var stage = Rows(read("stage"), 4).ToArray();
            caps = stage.ToDictionary(r => Integer(r[0], 1, int.MaxValue), r => Integer(r[1], 0, 9));
            rewardWaves = new HashSet<int>(stage.Where(r => Integer(r[2], 0, 1) == 1).Select(r => Integer(r[0], 1, int.MaxValue)));
            enemySynergyWaves = new HashSet<int>(stage.Where(r => Integer(r[3], 0, 1) == 1).Select(r => Integer(r[0], 1, int.MaxValue)));

            waves = Rows(read("spawns"), 8).Select(r => new SpawnRow
            {
                Wave = Integer(r[0], 1, TotalWaves), Batch = Integer(r[1], 1, 64), MinDelay = Number(r[2]), MaxDelay = Number(r[3]),
                Monster = r[4], Star = Integer(r[5], 1, 3), Count = Integer(r[6], 1, 32), Lane = Integer(r[7], 0, 2)
            }).GroupBy(r => r.Wave).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Batch).ToArray());
            for (int wave = 1; wave <= TotalWaves; wave++)
            {
                if (!caps.ContainsKey(wave) || !waves.TryGetValue(wave, out var rows))
                    throw new InvalidDataException("缺少波次或出怪配置：" + wave);
                var batches = rows.GroupBy(r => r.Batch).ToArray();
                for (int i = 0; i < batches.Length; i++)
                {
                    var first = batches[i].First();
                    if (first.Batch != i + 1 || first.MinDelay > first.MaxDelay ||
                        (i == 0 && first.MaxDelay != 0f) ||
                        batches[i].Any(r => r.MinDelay != first.MinDelay || r.MaxDelay != first.MaxDelay))
                        throw new InvalidDataException("小波序号或间隔无效：" + wave);
                }
                foreach (var row in rows)
                    if (Unit(row.Monster).Grade == UnitGrade.Hero)
                        throw new InvalidDataException("出怪表不可引用英雄");
            }
        }

        public UnitDefinition Unit(string id) => units[id];
        public float Tune(BattleTuning key) => tuning[key];
        public bool HasHero(string id) => !string.IsNullOrEmpty(id) && units.TryGetValue(id, out var value) && value.Grade == UnitGrade.Hero;
        public string Recruit(TroopKind symbol, BattleRandom random, string preferred = null)
        {
            var pool = recruits[(UnitJob)symbol];
            if (preferred != null && Array.IndexOf(pool, preferred) >= 0 && random.Next(100) < Tune(BattleTuning.RecruitHeroBias))
                return preferred;
            return pool[random.Next(pool.Length)];
        }
        public string LegacyHero(TroopKind symbol) => recruits[(UnitJob)symbol][0];
        public IReadOnlyList<string> RecruitPool(UnitJob job) => recruits[job];
        public int DeployCap(int wave) => caps[wave];
        public bool HasReward(int wave) => rewardWaves.Contains(wave);
        public bool HasEnemySynergy(int wave) => enemySynergyWaves.Contains(wave);
        public bool IsBoss(int wave) => waves[wave].Any(r => Unit(r.Monster).Grade == UnitGrade.Boss);

        public List<WaveSpawn> Schedule(int wave, int seed)
        {
            var random = new BattleRandom(unchecked(seed ^ (wave * 0x1e35a7bd)));
            var result = new List<WaveSpawn>();
            float at = 0;
            foreach (var batch in waves[wave].GroupBy(r => r.Batch))
            {
                var timing = batch.First();
                at += timing.MinDelay + (timing.MaxDelay - timing.MinDelay) * (random.Next(10001) / 10000f);
                foreach (var row in batch)
                    for (int i = 0; i < row.Count; i++)
                        result.Add(new WaveSpawn { MonsterId = row.Monster, Batch = row.Batch, Star = row.Star, Lane = row.Lane, At = at });
            }
            return result;
        }

#if !UNITY_5_3_OR_NEWER
        private static string ReadProjectFile(string name)
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                string path = Path.Combine(directory.FullName, "Assets", "LittleGame", "Resources", "BattleTables", name + ".csv");
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            throw new FileNotFoundException("找不到战斗配表：" + name);
        }
#endif

        private static IEnumerable<string[]> Rows(string data, int columns)
        {
            return data.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(line =>
            {
                var row = line.Split(',').Select(cell => cell.Trim()).ToArray();
                if (row.Length != columns) throw new InvalidDataException("配表列数错误：" + line);
                return row;
            });
        }

        private static T EnumValue<T>(string value) where T : struct
        {
            if (!Enum.TryParse(value, out T parsed) || !Enum.IsDefined(typeof(T), parsed)) throw new InvalidDataException("无效的值：" + value);
            return parsed;
        }

        private static float Number(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ||
                float.IsNaN(parsed) || float.IsInfinity(parsed) || parsed < 0)
                throw new InvalidDataException("无效的配表数值：" + value);
            return parsed;
        }

        private static float Positive(string value)
        {
            float parsed = Number(value);
            if (parsed <= 0f) throw new InvalidDataException("属性必须大于零");
            return parsed;
        }

        private static int Integer(string value, int min, int max)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < min || parsed > max)
                throw new InvalidDataException("值超出配表范围：" + value);
            return parsed;
        }
    }
}
