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
                Version = 4,
                Seed = Seed,
                Wave = Wave,
                MarchDistance = MarchDistance,
                Spins = Spins,
                Misses = misses,
                SpinCount = spinCount,
                MaxDeployed = maxDeployed,
                PopExp = popExp,
                NextId = nextId,
                RandomState = random.State,
                Phase = Phase,
                Speed = Speed,
                Reels = (TroopKind[])Reels.Clone(),
                Troops = troops.Select(t => new SaveTroop
                {
                    Id = t.Id,
                    Kind = t.Kind,
                    Star = t.Star,
                    Branch = t.Branch,
                    Lane = t.Lane,
                    Row = t.Row,
                    HomeX = t.HomeX,
                    HomeZ = t.HomeZ,
                    HasHomePos = t.Deployed
                }).ToList(),
                Rewards = rewards.ToList(),
                Pending = pending.ToList()
            };
        }

        public static BattleSession Restore(RunSave save)
        {
            if (save == null || save.Version < 1 || save.Version > 4 || save.Wave < 1 || save.Wave > 12 || save.RandomState == 0 || save.Spins < 0 || save.Spins > 4 || save.Misses < 0 || save.Misses > 2 || save.Phase == RunPhase.Fighting || !Enum.IsDefined(typeof(RunPhase), save.Phase))
                throw new ArgumentException("不支持或损坏的远征存档");
            if (save.Troops == null || save.Troops.Count > 15 || save.Troops.Select(t => t.Id).Distinct().Count() != save.Troops.Count || save.Troops.Any(t => t.Id < 1 || t.Id >= save.NextId || t.Star < 1 || t.Star > 3 || !Enum.IsDefined(typeof(TroopKind), t.Kind) || (t.Branch != SkillBranch.None && !BattleDefinitions.ValidBranch(t.Kind, t.Branch)) || (t.Star == 1 && t.Branch != SkillBranch.None) || t.Lane < -1 || t.Lane > 2 || t.Row < -1 || t.Row > 2 || ((t.Lane == -1) != (t.Row == -1))))
                throw new ArgumentException("存档军团无效");
            if (save.Troops.Count(t => t.Lane == -1) > 6 || (save.Phase != RunPhase.Preparation && !save.Troops.Any(t => t.Lane >= 0)))
                throw new ArgumentException("存档阵位冲突");
            if (save.Rewards == null || save.Rewards.Distinct().Count() != save.Rewards.Count || save.Rewards.Any(r => !Enum.IsDefined(typeof(RewardKind), r)) || save.Pending == null || save.Pending.Count > 3 || save.Pending.Any(k => !Enum.IsDefined(typeof(TroopKind), k)) || save.Reels == null || save.Reels.Length != 9 || save.Reels.Any(k => !Enum.IsDefined(typeof(TroopKind), k)) || (save.Speed != 1 && save.Speed != 2))
                throw new ArgumentException("存档奖励或转盘无效");

            var session = new BattleSession(save.Seed);
            session.troops.Clear(); session.enemies.Clear(); session.events.Clear(); session.eventSequence = 0;
            session.Wave = save.Wave;
            session.MarchDistance = save.Version >= 3 && save.MarchDistance >= 0f ? save.MarchDistance : (save.Wave - 1) * 25f;
            session.Spins = save.Spins; session.misses = save.Misses; session.spinCount = save.SpinCount;
            int deployedCount = save.Troops.Count(t => t.Lane >= 0);
            int restoredCap = save.Version >= 2 && save.MaxDeployed >= 6 && save.MaxDeployed <= 9 ? save.MaxDeployed : Math.Max(6, Math.Min(9, deployedCount));
            session.maxDeployed = Math.Max(restoredCap, Math.Min(9, deployedCount));
            session.popExp = save.Version >= 2 && save.PopExp >= 0 ? save.PopExp : 0;
            session.nextId = save.NextId; session.random.State = save.RandomState; session.Phase = save.Phase; session.Speed = save.Speed;
            session.Reels = (TroopKind[])save.Reels.Clone();
            session.rewards.AddRange(save.Rewards); session.pending.AddRange(save.Pending);
            foreach (var t in save.Troops)
            {
                var troop = new Troop
                {
                    Id = t.Id,
                    Kind = t.Kind,
                    Star = t.Star,
                    Branch = t.Branch,
                    Lane = t.Lane,
                    Row = t.Row,
                    HomeX = t.HasHomePos && BattleDefinitions.IsInsideDeployBox(t.HomeX, t.HomeZ) ? t.HomeX : BattleDefinitions.CellX(t.Row),
                    HomeZ = t.HasHomePos && BattleDefinitions.IsInsideDeployBox(t.HomeX, t.HomeZ) ? t.HomeZ : BattleDefinitions.CellZ(t.Lane)
                };
                session.ResetTroop(troop, snapToHome: true);
                session.troops.Add(troop);
            }
            session.Emit("run_restored", "已恢复最近准备或波后检查点；战中退出重打本波，随机结果不重抽");
            return session;
        }
    }
}
