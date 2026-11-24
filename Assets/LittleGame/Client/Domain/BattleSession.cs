using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    /// <summary>远征状态与唯一玩法指令入口。输入和 MCP 不得绕过此层。</summary>
    public sealed partial class BattleSession
    {
        private readonly List<Troop> troops = new List<Troop>();
        private readonly List<Enemy> enemies = new List<Enemy>();
        private readonly List<CombatEvent> events = new List<CombatEvent>();
        private readonly List<TroopKind> pending = new List<TroopKind>();
        private readonly List<RewardKind> rewards = new List<RewardKind>();
        private readonly List<StormZone> zones = new List<StormZone>();
        private BattleRandom random;
        private int nextId, misses, spinCount, spawnCursor;
        private long eventSequence;
        private List<WaveSpawn> spawns;
        private float accumulator, focusUntil, bossNextSlam, slamAt = -1;
        private int focusTarget, slamLane = -1;
        public int Seed { get; private set; }
        public RunPhase Phase { get; private set; }
        public int Wave { get; private set; }
        public int GateHp { get; private set; }
        public int Spins { get; private set; }
        public int SpinsPerWave => rewards.Contains(RewardKind.Reinforcements) ? 4 : 3;
        public bool FencesUp => !BattleDefinitions.IsBoss(Wave);
        public int Tactics { get; private set; }
        public AimKind Aim { get; private set; }
        public bool Paused { get; private set; }
        public int Speed { get; private set; } = 1;
        public float BattleTime { get; private set; }
        public int SlamLane => slamLane;
        public float SlamAt => slamAt;
        public long LastSequence => eventSequence;
        public TroopKind[] Reels { get; private set; } = new TroopKind[9];
        public IReadOnlyList<Troop> Troops => troops;
        public IReadOnlyList<Enemy> Enemies => enemies;
        public IReadOnlyList<TroopKind> Pending => pending;
        public IReadOnlyList<RewardKind> Rewards => rewards;
        public IReadOnlyList<StormZone> Zones => zones;
        public IReadOnlyList<WaveSpawn> Forecast => BattleDefinitions.Wave(Wave);
        public IEnumerable<CombatEvent> EventsAfter(long sequence) => events.Where(e => e.Sequence > sequence).ToArray();
        public BattleSession(int seed = 173) { Reset(seed); }
        private void Reset(int seed)
        {
            Seed = seed; random = new BattleRandom(seed); nextId = 1; Wave = 1; GateHp = 10; Spins = 3;
            misses = spinCount = spawnCursor = 0; Phase = RunPhase.Preparation; Aim = AimKind.None; Paused = false; Speed = 1; BattleTime = accumulator = 0;
            Tactics = 0; focusTarget = 0; focusUntil = 0; slamAt = -1; slamLane = -1;
            troops.Clear(); enemies.Clear(); pending.Clear(); rewards.Clear(); zones.Clear(); events.Clear(); Reels = new[] { TroopKind.Shield, TroopKind.Archer, TroopKind.Storm, TroopKind.Archer, TroopKind.Storm, TroopKind.Frost, TroopKind.Storm, TroopKind.Frost, TroopKind.Shield };
            // 起始三盾和三输出保障三路可玩；教学转盘仍需玩家自己部署。
            for (int lane = 0; lane < 3; lane++) { AddTroop(TroopKind.Shield, lane, 0); AddTroop(lane == 1 ? TroopKind.Storm : TroopKind.Archer, lane, 1); }
            Emit("run_started", "远征开始：先看敌情，再招募布阵");
        }
        private void AddTroop(TroopKind kind, int lane = -1, int row = -1)
        {
            var t = new Troop { Id = nextId++, Kind = kind, Lane = lane, Row = row };
            ResetTroop(t); troops.Add(t);
        }
        private void ResetTroop(Troop t)
        {
            t.X = t.Deployed ? BattleDefinitions.LaneX(t.Lane) : 0; t.Z = t.Deployed ? BattleDefinitions.RowZ(t.Row) : 0;
            t.Hp = t.MaxHp = BattleDefinitions.Health(t); t.Cooldown = .25f; t.SkillCooldown = .8f + ((int)t.Kind * .12f); t.Shield = t.ShieldUntil = 0;
        }
        private CommandResult Reject(string reason) => new CommandResult(false, reason);
        public CommandResult Execute(BattleCommand c)
        {
            if (c == null || !Enum.IsDefined(typeof(CommandKind), c.Kind)) return Reject("未知玩法指令");
            if (c.Kind == CommandKind.NewRun) { Reset(c.Seed); return new CommandResult(true, "已重开"); }
            if (c.Kind == CommandKind.SetSpeed) { if (c.Speed != 1 && c.Speed != 2) return Reject("只支持1倍或2倍"); Speed = c.Speed; return new CommandResult(true, "已切换速度"); }
            if (c.Kind == CommandKind.CancelAim) { Aim = AimKind.None; return new CommandResult(true, "已取消瞄准"); }
            if (c.Kind == CommandKind.Pause) { if (Phase != RunPhase.Fighting) return Reject("仅战斗中可暂停"); Paused = c.Flag; return new CommandResult(true, Paused ? "已暂停" : "继续战斗"); }
            if (c.Kind == CommandKind.NextWave)
            {
                if (Phase != RunPhase.WaveResult) return Reject("请先完成本波与奖励选择");
                Wave++; Phase = RunPhase.Preparation; Spins = SpinsPerWave; BattleTime = 0; Paused = false; Aim = AimKind.None;
                enemies.Clear(); zones.Clear(); foreach (var t in troops) ResetTroop(t);
                Emit("wave_prepared", FencesUp ? "隔栏升起，查看三路敌情" : "隔栏撤下，准备首领战"); return new CommandResult(true, "进入下一波");
            }
            if (c.Kind == CommandKind.ChooseReward)
            {
                if (Phase != RunPhase.Reward || !Enum.IsDefined(typeof(RewardKind), c.Reward) || rewards.Contains(c.Reward)) return Reject("当前没有此奖励可选");
                rewards.Add(c.Reward); Phase = RunPhase.WaveResult; Emit("reward_chosen", BattleDefinitions.Name(c.Reward)); return new CommandResult(true, "奖励已生效");
            }
            if (Phase == RunPhase.Fighting) return CombatCommand(c);
            if (Phase != RunPhase.Preparation) return Reject("当前阶段不能操作军团");
            if (c.Kind == CommandKind.Spin) return Spin();
            if (c.Kind == CommandKind.StartWave) return StartWave();
            var unit = troops.Find(t => t.Id == c.UnitId);
            if (unit == null) return Reject("单位不存在");
            switch (c.Kind)
            {
                case CommandKind.Deploy:
                    if (c.Lane < 0 || c.Lane > 2 || c.Row < 0 || c.Row > 2) return Reject("阵位必须在三行三列内");
                    var occupied = troops.Find(t => t.Id != unit.Id && t.Lane == c.Lane && t.Row == c.Row);
                    if (occupied != null) { occupied.Lane = unit.Lane; occupied.Row = unit.Row; ResetTroop(occupied); }
                    unit.Lane = c.Lane; unit.Row = c.Row; ResetTroop(unit); Emit("deployed", "已部署" + BattleDefinitions.Name(unit.Kind), unit.Id, 0, c.Lane); break;
                case CommandKind.Recall:
                    if (!unit.Deployed) return Reject("该单位已在候补区");
                    if (troops.Count(t => !t.Deployed) >= 6) return Reject("候补区已满");
                    if (troops.Count(t => t.Deployed) <= 1) return Reject("至少保留一名上阵单位");
                    unit.Lane = unit.Row = -1; break;
                case CommandKind.Recycle:
                    if (unit.Deployed) return Reject("先召回候补再遣散，避免误删阵线");
                    troops.Remove(unit); Emit("recycled", "已遣散候补", unit.Id); break;
                case CommandKind.Merge: return Merge(unit, c.OtherId, c.ThirdId);
                case CommandKind.ChooseBranch:
                    if (unit.Star < 2 || unit.Branch != SkillBranch.None || !BattleDefinitions.ValidBranch(unit.Kind, c.Branch)) return Reject("分支不可选择或已经锁定");
                    unit.Branch = c.Branch; Emit("branch_chosen", BattleDefinitions.Name(c.Branch), unit.Id); break;
                default: return Reject("该指令不适用于准备阶段");
            }
            DrainPending(); return new CommandResult(true, "操作成功");
        }
        private CommandResult Merge(Troop host, int aId, int bId)
        {
            var a = troops.Find(t => t.Id == aId); var b = troops.Find(t => t.Id == bId);
            if (a == null || b == null || a == b || a == host || b == host) return Reject("合成需要三个不同单位");
            if (host.Star >= 3 || a.Star != host.Star || b.Star != host.Star || a.Kind != host.Kind || b.Kind != host.Kind) return Reject("需要三个同职业同星单位，最高三星");
            // 分支作为宿主身份保留；素材分支不会迁入。
            host.Star++; troops.Remove(a); troops.Remove(b); ResetTroop(host); DrainPending();
            Emit("merged", "升为" + host.Star + "星，二星需选择分支", host.Id); return new CommandResult(true, "合成成功");
        }
        private CommandResult Spin()
        {
            if (Spins <= 0) return Reject("本波招募次数已用完");
            if (pending.Count > 0) return Reject("请先处理暂存单位：部署、合成或遣散候补");
            Spins--; spinCount++;
            for (int i = 0; i < 9; i++) Reels[i] = (TroopKind)random.Next(4);
            // 明确标为教学预设，第三转以后使用固定算法随机。
            bool tutorial = spinCount <= 2;
            if (tutorial) { int row = spinCount - 1; for (int col = 0; col < 3; col++) Reels[row * 3 + col] = spinCount == 1 ? TroopKind.Shield : TroopKind.Archer; }
            bool guarantee = misses >= 2;
            if (guarantee) { int row = random.Next(3); var kind = Reels[row * 3]; Reels[row * 3 + 1] = Reels[row * 3 + 2] = kind; }
            int won = 0;
            for (int row = 0; row < 3; row++) if (Reels[row * 3] == Reels[row * 3 + 1] && Reels[row * 3] == Reels[row * 3 + 2]) { pending.Add(Reels[row * 3]); won++; }
            misses = won == 0 ? misses + 1 : 0; DrainPending();
            Emit("spin", (tutorial ? "教学预设 · " : guarantee ? "保底触发 · " : "随机招募 · ") + "出兵" + won + "名"); return new CommandResult(true, "获得" + won + "名单位");
        }
        private void DrainPending() { while (pending.Count > 0 && troops.Count(t => !t.Deployed) < 6) { AddTroop(pending[0]); pending.RemoveAt(0); } }
        private void Emit(string type, string message, int source = 0, int target = 0, int lane = -1, float amount = 0)
        {
            events.Add(new CombatEvent { Sequence = ++eventSequence, Type = type, Message = message, Source = source, Target = target, Lane = lane, Amount = amount, Time = BattleTime });
            if (events.Count > 600) events.RemoveRange(0, events.Count - 600);
        }
        public object Snapshot() => new
        {
            schemaVersion = 1, phase = Phase.ToString(), seed = Seed, wave = Wave, totalWaves = 12, gateHp = GateHp,
            spins = Spins, spinsPerWave = SpinsPerWave, consecutiveMisses = misses, teaching = spinCount < 2,
            fencesUp = FencesUp, tactics = Tactics, aim = Aim.ToString(), paused = Paused, speed = Speed, battleTime = BattleTime,
            forecast = Forecast.Select(e => new { kind = e.Kind.ToString(), name = BattleDefinitions.Name(e.Kind), lane = e.Lane, at = e.At }),
            units = troops.Select(t => new { id = t.Id, kind = t.Kind.ToString(), name = BattleDefinitions.Name(t.Kind), star = t.Star, branch = t.Branch.ToString(), lane = t.Lane, row = t.Row, x = t.X, z = t.Z, hp = t.Hp, maxHp = t.MaxHp, shield = t.Shield, cooldown = t.Cooldown, skillCooldown = t.SkillCooldown }),
            enemies = enemies.Select(e => new { id = e.Id, kind = e.Kind.ToString(), lane = e.Lane, x = e.X, z = e.Z, hp = e.Hp, maxHp = e.MaxHp, alive = e.Alive, escaped = e.Escaped, frozenUntil = e.FrozenUntil }),
            pending = pending.Select(k => k.ToString()), rewards = rewards.Select(r => r.ToString()), reels = Reels.Select(k => k.ToString()),
            rewardOptions = Phase == RunPhase.Reward ? Enum.GetValues(typeof(RewardKind)).Cast<RewardKind>().Where(r => !rewards.Contains(r)).Select(r => r.ToString()).ToArray() : Array.Empty<string>(),
            bossTelegraph = new { lane = slamLane, impactAt = slamAt }, lastEventSequence = eventSequence,
            events = events.Skip(Math.Max(0, events.Count - 35)).ToArray(), availableActions = AvailableActions()
        };
        private string[] AvailableActions()
        {
            if (Phase == RunPhase.Preparation)
            {
                var actions = new List<string> { "deploy", "restart" };
                if (Spins > 0 && pending.Count == 0) actions.Add("spin");
                if (troops.Count(t => t.Deployed) > 1 && troops.Count(t => !t.Deployed) < 6) actions.Add("recall");
                if (troops.Any(t => !t.Deployed)) actions.Add("recycle");
                if (troops.GroupBy(t => new { t.Kind, t.Star }).Any(g => g.Key.Star < 3 && g.Count() >= 3)) actions.Add("merge");
                if (troops.Any(t => t.Star >= 2 && t.Branch == SkillBranch.None)) actions.Add("choose_branch");
                else if (pending.Count == 0 && troops.Any(t => t.Deployed)) actions.Add("start_wave");
                return actions.ToArray();
            }
            if (Phase == RunPhase.Fighting) return Tactics > 0 ? new[] { "aim", "focus", "protect", "cancel_aim", "pause", "set_speed", "restart" } : new[] { "pause", "set_speed", "restart" };
            return Phase == RunPhase.Reward ? new[] { "choose_reward", "restart" } : Phase == RunPhase.WaveResult ? new[] { "next_wave", "restart" } : new[] { "restart" };
        }
    }
}
