using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    /// <summary>横向无限推进远征状态与唯一玩法指令入口。输入和 MCP 不得绕过此层。</summary>
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
        private int maxDeployed = 6, popExp;
        private bool lastWinRowMid, lastWinColMid, enraged;
        private long eventSequence;
        private List<WaveSpawn> spawns;
        private float accumulator, focusUntil, bossNextSlam, slamAt = -1;
        private int focusTarget, slamLane = -1;

        public int Seed { get; private set; }
        public RunPhase Phase { get; private set; }
        public int Wave { get; private set; }
        public float MarchDistance { get; private set; }
        public int Spins { get; private set; }
        public int SpinsPerWave => rewards.Contains(RewardKind.Reinforcements) ? 4 : 3;
        public int MaxDeployed => maxDeployed;
        public int DeployedCount => troops.Count(t => t.Deployed);
        public int AliveTroopCount => troops.Count(t => t.Deployed && t.Alive);
        public bool IsRegrouping => Phase != RunPhase.Fighting && troops.Any(t => t.NeedsRegroup);
        public int PopExp => popExp;
        public int PopExpNeeded => maxDeployed >= 9 ? 0 : (maxDeployed == 6 ? 4 : maxDeployed == 7 ? 6 : 8);
        public bool LastWinRowMid => lastWinRowMid;
        public bool LastWinColMid => lastWinColMid;
        public bool Enraged => enraged;
        public int Tactics { get; private set; }
        public AimKind Aim { get; private set; }
        public bool Paused { get; private set; }
        public int Speed { get; private set; } = 1;
        public float BattleTime { get; private set; }
        public int SlamLane => slamLane;
        public float SlamAt => slamAt;
        public int FocusTarget => focusTarget;
        public float FocusUntil => focusUntil;
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

        public bool CanMerge(Troop host)
        {
            if (host == null || host.Star >= 3) return false;
            return troops.Count(t => t.Kind == host.Kind && t.Star == host.Star) >= 3;
        }

        private void Reset(int seed)
        {
            Seed = seed; random = new BattleRandom(seed); nextId = 1; Wave = 1; MarchDistance = 0f; Spins = 3;
            maxDeployed = 6; popExp = 0; lastWinRowMid = lastWinColMid = enraged = false;
            misses = spinCount = spawnCursor = 0; Phase = RunPhase.Preparation; Aim = AimKind.None; Paused = false; Speed = 1; BattleTime = accumulator = 0;
            Tactics = 0; focusTarget = 0; focusUntil = 0; slamAt = -1; slamLane = -1;
            troops.Clear(); enemies.Clear(); pending.Clear(); rewards.Clear(); zones.Clear(); events.Clear();
            Reels = new[] { TroopKind.Shield, TroopKind.Archer, TroopKind.Storm, TroopKind.Archer, TroopKind.Storm, TroopKind.Frost, TroopKind.Storm, TroopKind.Frost, TroopKind.Shield };
            for (int lane = 0; lane < 3; lane++)
            {
                AddTroop(TroopKind.Shield, lane, 0);
                AddTroop(lane == 1 ? TroopKind.Storm : TroopKind.Archer, lane, 1);
            }
            Emit("run_started", "军团集结：在白色布阵区内自由拖放，迎战来敌");
        }

        private void AddTroop(TroopKind kind, int lane = -1, int row = -1)
        {
            var t = new Troop { Id = nextId++, Kind = kind, Lane = lane, Row = row };
            if (t.Deployed)
            {
                t.HomeX = BattleDefinitions.CellX(row);
                t.HomeZ = BattleDefinitions.CellZ(lane);
            }
            ResetTroop(t, snapToHome: true);
            troops.Add(t);
        }

        private void ResetTroop(Troop t, bool snapToHome = true)
        {
            if (t.Deployed)
            {
                if (!BattleDefinitions.IsInsideDeployBox(t.HomeX, t.HomeZ))
                {
                    t.HomeX = Clamp(BattleDefinitions.CellX(t.Row), BattleDefinitions.DeployMinX, BattleDefinitions.DeployMaxX);
                    t.HomeZ = Clamp(BattleDefinitions.CellZ(t.Lane), BattleDefinitions.DeployMinZ, BattleDefinitions.DeployMaxZ);
                }
                if (snapToHome)
                {
                    t.X = t.HomeX;
                    t.Z = t.HomeZ;
                    t.FacingYaw = 90f;
                }
            }
            else
            {
                t.X = t.Z = t.HomeX = t.HomeZ = 0f;
                t.FacingYaw = 90f;
            }
            t.TurnSpeed = BattleDefinitions.TurnSpeed(t.Kind);
            t.MoveSpeed = BattleDefinitions.MoveSpeed(t.Kind);
            t.Hp = t.MaxHp = BattleDefinitions.Health(t);
            t.Cooldown = .25f;
            t.SkillCooldown = .8f + ((int)t.Kind * .12f);
            t.Shield = t.ShieldUntil = 0f;
        }

        /// <summary>
        /// 波后非战斗阶段平滑推进：偏离白色布阵框内编队点的单位以各自 MoveSpeed * 2.45 快速跑回白框内，不瞬移。
        /// </summary>
        public void StepRegroup(float dt)
        {
            if (Phase == RunPhase.Fighting || dt <= 0f) return;
            float clampedDt = Math.Min(0.1f, dt);
            foreach (var t in troops)
            {
                if (!t.Deployed) continue;
                float dx = t.HomeX - t.X;
                float dz = t.HomeZ - t.Z;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dist > 0.04f)
                {
                    float regroupSpeed = Math.Max(2.2f, t.MoveSpeed) * 2.45f;
                    float step = regroupSpeed * clampedDt;
                    float moveYaw = BattleDefinitions.VectorToYaw(dx, dz, t.FacingYaw);
                    t.FacingYaw = BattleDefinitions.MoveTowardsAngle(t.FacingYaw, moveYaw, t.TurnSpeed * 1.8f * clampedDt);
                    if (dist <= step)
                    {
                        t.X = t.HomeX;
                        t.Z = t.HomeZ;
                    }
                    else
                    {
                        t.X += dx / dist * step;
                        t.Z += dz / dist * step;
                    }
                }
                else
                {
                    t.X = t.HomeX;
                    t.Z = t.HomeZ;
                    t.FacingYaw = BattleDefinitions.MoveTowardsAngle(t.FacingYaw, 90f, t.TurnSpeed * 1.6f * clampedDt);
                }
            }
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
                lastWinRowMid = lastWinColMid = enraged = false;
                MarchDistance += 25f;
                enemies.Clear(); zones.Clear();
                foreach (var t in troops) ResetTroop(t, snapToHome: false);
                Emit("wave_prepared", BattleDefinitions.IsBoss(Wave) ? "前方巨像拦路，全军准备迎战" : "军团满血推进至下一战区");
                return new CommandResult(true, "进入下一波");
            }
            if (c.Kind == CommandKind.ChooseReward)
            {
                if (Phase != RunPhase.Reward || !Enum.IsDefined(typeof(RewardKind), c.Reward) || rewards.Contains(c.Reward)) return Reject("当前没有此奖励可选");
                rewards.Add(c.Reward); Phase = RunPhase.WaveResult; Emit("reward_chosen", BattleDefinitions.Name(c.Reward));
                return new CommandResult(true, "奖励已生效");
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
                    return ExecuteDeploy(unit, c);
                case CommandKind.Recall:
                    if (!unit.Deployed) return Reject("该单位已在候补区");
                    if (troops.Count(t => !t.Deployed) >= 6) return Reject("候补区已满");
                    if (troops.Count(t => t.Deployed) <= 1) return Reject("至少保留一名上阵单位");
                    unit.Lane = unit.Row = -1;
                    unit.HomeX = unit.HomeZ = unit.X = unit.Z = 0f;
                    break;
                case CommandKind.Recycle:
                    if (unit.Deployed) return Reject("先召回候补再遣散，避免误删阵线");
                    troops.Remove(unit); Emit("recycled", "已遣散候补", unit.Id);
                    break;
                case CommandKind.Merge: return Merge(unit, c.OtherId, c.ThirdId);
                case CommandKind.ChooseBranch:
                    if (unit.Star < 2 || unit.Branch != SkillBranch.None || !BattleDefinitions.ValidBranch(unit.Kind, c.Branch)) return Reject("分支不可选择或已经锁定");
                    unit.Branch = c.Branch; Emit("branch_chosen", BattleDefinitions.Name(c.Branch), unit.Id);
                    break;
                default: return Reject("该指令不适用于准备阶段");
            }
            DrainPending(); return new CommandResult(true, "操作成功");
        }

        private CommandResult ExecuteDeploy(Troop unit, BattleCommand c)
        {
            float tx, tz;
            int targetLane, targetRow;
            if (c.UseWorldPos)
            {
                if (float.IsNaN(c.TargetX) || float.IsInfinity(c.TargetX) || float.IsNaN(c.TargetZ) || float.IsInfinity(c.TargetZ)
                    || !BattleDefinitions.IsInsideDeployBox(c.TargetX, c.TargetZ))
                {
                    return Reject("拖动超出白色布阵范围，放置失败");
                }
                tx = c.TargetX;
                tz = c.TargetZ;
                targetLane = BattleDefinitions.NearestLane(tz);
                targetRow = BattleDefinitions.NearestRow(tx);
            }
            else
            {
                if (c.Lane < 0 || c.Lane > 2 || c.Row < 0 || c.Row > 2) return Reject("阵位必须在白色布阵范围内");
                targetLane = c.Lane;
                targetRow = c.Row;
                tx = BattleDefinitions.CellX(targetRow);
                tz = BattleDefinitions.CellZ(targetLane);
            }

            Troop occupied = c.UseWorldPos
                ? troops.Where(t => t.Id != unit.Id && t.Deployed && Distance(t.HomeX, t.HomeZ, tx, tz) <= 0.78f)
                    .OrderBy(t => Distance(t.HomeX, t.HomeZ, tx, tz))
                    .FirstOrDefault()
                : troops.Find(t => t.Id != unit.Id && t.Deployed && (Distance(t.HomeX, t.HomeZ, tx, tz) <= 0.75f || (t.Lane == targetLane && t.Row == targetRow)));

            if (!unit.Deployed && occupied == null && DeployedCount >= maxDeployed)
                return Reject($"人口已满（{DeployedCount}/{maxDeployed}），拖至场上单位替换或过波升级");

            if (occupied != null)
            {
                int oldLane = unit.Lane, oldRow = unit.Row;
                float oldHx = unit.HomeX, oldHz = unit.HomeZ;
                unit.Lane = occupied.Lane;
                unit.Row = occupied.Row;
                unit.HomeX = occupied.HomeX;
                unit.HomeZ = occupied.HomeZ;
                occupied.Lane = oldLane;
                occupied.Row = oldRow;
                occupied.HomeX = oldHx;
                occupied.HomeZ = oldHz;
                ResetTroop(occupied, snapToHome: true);
                ResetTroop(unit, snapToHome: true);
            }
            else
            {
                ResolveFreeDropOverlap(unit.Id, ref tx, ref tz);
                unit.Lane = BattleDefinitions.NearestLane(tz);
                unit.Row = BattleDefinitions.NearestRow(tx);
                unit.HomeX = tx;
                unit.HomeZ = tz;
                ResetTroop(unit, snapToHome: true);
            }

            Emit("deployed", "已部署" + BattleDefinitions.Name(unit.Kind), unit.Id, 0, unit.Lane);
            DrainPending();
            return new CommandResult(true, "操作成功");
        }

        private void ResolveFreeDropOverlap(int unitId, ref float x, ref float z)
        {
            const float minSpacing = 0.76f;
            for (int iter = 0; iter < 3; iter++)
            {
                foreach (var other in troops)
                {
                    if (other.Id == unitId || !other.Deployed) continue;
                    float dx = x - other.HomeX;
                    float dz = z - other.HomeZ;
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d < minSpacing)
                    {
                        float nx = d > 1e-4f ? dx / d : 0f;
                        float nz = d > 1e-4f ? dz / d : 1f;
                        float push = (minSpacing - d) + 0.02f;
                        x = Clamp(x + nx * push, BattleDefinitions.DeployMinX + 0.15f, BattleDefinitions.DeployMaxX - 0.15f);
                        z = Clamp(z + nz * push, BattleDefinitions.DeployMinZ + 0.15f, BattleDefinitions.DeployMaxZ - 0.15f);
                    }
                }
            }
        }

        private CommandResult Merge(Troop host, int aId, int bId)
        {
            var a = troops.Find(t => t.Id == aId); var b = troops.Find(t => t.Id == bId);
            if (a == null || b == null || a == b || a == host || b == host) return Reject("合成需要三个不同单位");
            if (host.Star >= 3 || a.Star != host.Star || b.Star != host.Star || a.Kind != host.Kind || b.Kind != host.Kind) return Reject("需要三个同职业同星单位，最高三星");
            host.Star++; troops.Remove(a); troops.Remove(b); ResetTroop(host, snapToHome: true); DrainPending();
            Emit("merged", "升为" + host.Star + "星，二星需选择分支", host.Id);
            return new CommandResult(true, "合成成功");
        }

        private CommandResult Spin()
        {
            if (Spins <= 0) return Reject("本波招募次数已用完");
            if (pending.Count > 0) return Reject("请先处理暂存单位：部署、合成或遣散候补");
            Spins--; spinCount++;
            for (int i = 0; i < 9; i++) Reels[i] = (TroopKind)random.Next(4);
            bool tutorial = spinCount <= 2;
            if (tutorial)
            {
                if (spinCount == 1) { Reels[3] = Reels[4] = Reels[5] = TroopKind.Shield; }
                else { Reels[1] = Reels[4] = Reels[7] = TroopKind.Archer; }
            }
            bool guarantee = misses >= 2;
            if (guarantee)
            {
                bool vertical = random.Next(2) == 1;
                var kind = Reels[4];
                if (vertical) Reels[1] = Reels[7] = kind;
                else Reels[3] = Reels[5] = kind;
            }
            lastWinRowMid = Reels[3] == Reels[4] && Reels[4] == Reels[5];
            lastWinColMid = Reels[1] == Reels[4] && Reels[4] == Reels[7];
            int won = 0;
            if (lastWinRowMid) { pending.Add(Reels[4]); won++; }
            if (lastWinColMid) { pending.Add(Reels[4]); won++; }
            misses = won == 0 ? misses + 1 : 0; DrainPending();
            string lineText = lastWinRowMid && lastWinColMid ? "十字双连！" : lastWinRowMid ? "中横三同！" : lastWinColMid ? "中竖三同！" : "";
            Emit("spin", (tutorial ? "教学预设 · " : guarantee ? "保底触发 · " : "随机招募 · ") + lineText + "出兵" + won + "名");
            return new CommandResult(true, won > 0 ? lineText + "获得" + won + "名单位" : "未连成十字三同");
        }

        internal void GrantWavePopExp(int amount)
        {
            if (maxDeployed >= 9 || amount <= 0) return;
            popExp += amount;
            while (maxDeployed < 9 && popExp >= PopExpNeeded)
            {
                popExp -= PopExpNeeded;
                maxDeployed++;
                Emit("pop_up", $"人口上限提升至 {maxDeployed}");
            }
            if (maxDeployed >= 9) popExp = 0;
        }

        private void DrainPending() { while (pending.Count > 0 && troops.Count(t => !t.Deployed) < 6) { AddTroop(pending[0]); pending.RemoveAt(0); } }

        private void Emit(string type, string message, int source = 0, int target = 0, int lane = -1, float amount = 0)
        {
            events.Add(new CombatEvent { Sequence = ++eventSequence, Type = type, Message = message, Source = source, Target = target, Lane = lane, Amount = amount, Time = BattleTime });
            if (events.Count > 600) events.RemoveRange(0, events.Count - 600);
        }

        public object Snapshot() => new
        {
            schemaVersion = 4,
            layout = "horizontal-free-deploy",
            phase = Phase.ToString(),
            seed = Seed,
            wave = Wave,
            totalWaves = BattleDefinitions.TotalWaves,
            marchDistance = Math.Round(MarchDistance, 1),
            aliveTroops = AliveTroopCount,
            regrouping = IsRegrouping,
            enraged = enraged,
            deployBox = new { minX = BattleDefinitions.DeployMinX, maxX = BattleDefinitions.DeployMaxX, minZ = BattleDefinitions.DeployMinZ, maxZ = BattleDefinitions.DeployMaxZ },
            spins = Spins,
            spinsPerWave = SpinsPerWave,
            consecutiveMisses = misses,
            teaching = spinCount < 2,
            deployedCount = DeployedCount,
            maxDeployed = maxDeployed,
            popExp = popExp,
            popExpNeeded = PopExpNeeded,
            winRowMid = lastWinRowMid,
            winColMid = lastWinColMid,
            tactics = Tactics,
            aim = Aim.ToString(),
            paused = Paused,
            speed = Speed,
            battleTime = BattleTime,
            forecast = Forecast.Select(e => new { kind = e.Kind.ToString(), name = BattleDefinitions.Name(e.Kind), lane = e.Lane, at = e.At }),
            units = troops.Select(t => new
            {
                id = t.Id,
                kind = t.Kind.ToString(),
                name = BattleDefinitions.Name(t.Kind),
                star = t.Star,
                branch = t.Branch.ToString(),
                lane = t.Lane,
                row = t.Row,
                homeX = Math.Round(t.HomeX, 2),
                homeZ = Math.Round(t.HomeZ, 2),
                x = Math.Round(t.X, 2),
                z = Math.Round(t.Z, 2),
                facingYaw = Math.Round(t.FacingYaw, 1),
                turnSpeed = t.TurnSpeed,
                moveSpeed = t.MoveSpeed,
                canMerge = CanMerge(t),
                hp = t.Hp,
                maxHp = t.MaxHp,
                shield = t.Shield,
                cooldown = t.Cooldown,
                skillCooldown = t.SkillCooldown
            }),
            enemies = enemies.Select(e => new
            {
                id = e.Id,
                kind = e.Kind.ToString(),
                lane = e.Lane,
                x = Math.Round(e.X, 2),
                z = Math.Round(e.Z, 2),
                facingYaw = Math.Round(e.FacingYaw, 1),
                turnSpeed = e.TurnSpeed,
                moveSpeed = e.MoveSpeed,
                hp = e.Hp,
                maxHp = e.MaxHp,
                alive = e.Alive,
                frozenUntil = e.FrozenUntil
            }),
            pending = pending.Select(k => k.ToString()),
            rewards = rewards.Select(r => r.ToString()),
            reels = Reels.Select(k => k.ToString()),
            rewardOptions = Phase == RunPhase.Reward ? Enum.GetValues(typeof(RewardKind)).Cast<RewardKind>().Where(r => !rewards.Contains(r)).Select(r => r.ToString()).ToArray() : Array.Empty<string>(),
            bossTelegraph = new { lane = slamLane, impactAt = slamAt },
            lastEventSequence = eventSequence,
            events = events.Skip(Math.Max(0, events.Count - 35)).ToArray(),
            availableActions = AvailableActions()
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
