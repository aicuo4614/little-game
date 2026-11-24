using System;
using System.Linq;

namespace LittleGame.Combat
{
    public sealed partial class BattleSession
    {
        public RunSave Save()
        {
            if (Phase == RunPhase.Fighting) throw new InvalidOperationException("战中使用开战前检查点恢复，不保存半帧状态");
            return new RunSave
            {
                Seed = Seed, Wave = Wave, GateHp = GateHp, Spins = Spins, Misses = misses, SpinCount = spinCount,
                NextId = nextId, RandomState = random.State, Phase = Phase, Speed = Speed, Reels = (TroopKind[])Reels.Clone(),
                Troops = troops.Select(t => new SaveTroop { Id = t.Id, Kind = t.Kind, Star = t.Star, Branch = t.Branch, Lane = t.Lane, Row = t.Row }).ToList(),
                Rewards = rewards.ToList(), Pending = pending.ToList()
            };
        }
        public static BattleSession Restore(RunSave save)
        {
            if (save == null || save.Version != 1 || save.Wave < 1 || save.Wave > 12 || save.GateHp < 0 || save.GateHp > 10 || save.RandomState == 0 || save.Spins < 0 || save.Spins > 4 || save.Misses < 0 || save.Misses > 2 || save.Phase == RunPhase.Fighting || !Enum.IsDefined(typeof(RunPhase), save.Phase)) throw new ArgumentException("不支持或损坏的远征存档");
            if (save.Troops == null || save.Troops.Count > 15 || save.Troops.Select(t => t.Id).Distinct().Count() != save.Troops.Count || save.Troops.Any(t => t.Id < 1 || t.Id >= save.NextId || t.Star < 1 || t.Star > 3 || !Enum.IsDefined(typeof(TroopKind), t.Kind) || (t.Branch != SkillBranch.None && !BattleDefinitions.ValidBranch(t.Kind, t.Branch)) || (t.Star == 1 && t.Branch != SkillBranch.None) || t.Lane < -1 || t.Lane > 2 || t.Row < -1 || t.Row > 2 || ((t.Lane == -1) != (t.Row == -1)))) throw new ArgumentException("存档军团无效");
            if (save.Troops.Count(t => t.Lane == -1) > 6 || save.Troops.Where(t => t.Lane >= 0).GroupBy(t => t.Lane * 3 + t.Row).Any(g => g.Count() > 1) || !save.Troops.Any(t => t.Lane >= 0)) throw new ArgumentException("存档阵位冲突");
            if (save.Rewards == null || save.Rewards.Distinct().Count() != save.Rewards.Count || save.Rewards.Any(r => !Enum.IsDefined(typeof(RewardKind), r)) || save.Pending == null || save.Pending.Count > 3 || save.Pending.Any(k => !Enum.IsDefined(typeof(TroopKind), k)) || save.Reels == null || save.Reels.Length != 9 || save.Reels.Any(k => !Enum.IsDefined(typeof(TroopKind), k)) || (save.Speed != 1 && save.Speed != 2)) throw new ArgumentException("存档奖励或转盘无效");
            var session = new BattleSession(save.Seed);
            session.troops.Clear(); session.enemies.Clear(); session.events.Clear(); session.eventSequence = 0;
            session.Wave = save.Wave; session.GateHp = save.GateHp; session.Spins = save.Spins; session.misses = save.Misses; session.spinCount = save.SpinCount;
            session.nextId = save.NextId; session.random.State = save.RandomState; session.Phase = save.Phase; session.Speed = save.Speed; session.Reels = (TroopKind[])save.Reels.Clone();
            session.rewards.AddRange(save.Rewards); session.pending.AddRange(save.Pending);
            foreach (var t in save.Troops) { var troop = new Troop { Id = t.Id, Kind = t.Kind, Star = t.Star, Branch = t.Branch, Lane = t.Lane, Row = t.Row }; session.ResetTroop(troop); session.troops.Add(troop); }
            session.Emit("run_restored", "已恢复最近准备或波后检查点；战中退出重打本波，随机结果不重抽"); return session;
        }
    }
}
