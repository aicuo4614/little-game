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
        private readonly List<string> pending = new List<string>();
        private readonly List<RewardKind> rewards = new List<RewardKind>();
        private readonly List<StormZone> zones = new List<StormZone>();
        private IBattleNavigation navigation;
        public void AttachNavigation(IBattleNavigation backend) { navigation?.Reset(); navigation = backend ?? throw new ArgumentNullException(nameof(backend)); navigation.Reset(); }
        private readonly List<BattleNavigation.AgentInput> navInputs = new List<BattleNavigation.AgentInput>(32);
        private readonly List<BattleNavigation.AgentOutput> navOutputs = new List<BattleNavigation.AgentOutput>(32);

        private BattleRandom random;
        private int nextId, misses, spinCount, spawnCursor;
        private int maxDeployed = 6, popExp;
        private bool lastWinRowMid, lastWinColMid, enraged;
        private long eventSequence;
        private List<WaveSpawn> spawns;
        private float accumulator, slamAt = -1;
        private int slamLane = -1;

        public int Seed { get; private set; }
        public long NavigationCpuTicks { get; private set; }
        public RunPhase Phase { get; private set; }
        public int Wave { get; private set; }
        public float MarchDistance { get; private set; }
        public int Spins { get; private set; }
        public int SpinsPerWave => rewards.Contains(RewardKind.Reinforcements) ? 4 : 3;
        public int MaxDeployed
        {
            get
            {
                int cap = BattleTables.Current.DeployCap(Wave);
                return cap > 0 ? Math.Min(maxDeployed, cap) : maxDeployed;
            }
        }
        public int DeployedCount => troops.Count(t => t.Deployed);
        public int AliveTroopCount => troops.Count(t => t.Deployed && t.Alive);
        public bool IsRegrouping => Phase != RunPhase.Fighting && troops.Any(t => t.NeedsRegroup);
        public int PopExp => popExp;
        public int PopExpNeeded => maxDeployed >= 9 ? 0 : (maxDeployed == 6 ? 4 : maxDeployed == 7 ? 6 : 8);
        public bool LastWinRowMid => lastWinRowMid;
        public bool LastWinColMid => lastWinColMid;
        public bool Enraged => enraged;
        public bool Paused { get; private set; }
        public int Speed { get; private set; } = 1;
        public float BattleTime { get; private set; }
        public int SlamLane => slamLane;
        public float SlamAt => slamAt;
        public long LastSequence => eventSequence;
        public long LastSpinSequence { get; private set; }
        public TroopKind[] Reels { get; private set; } = new TroopKind[9];
        public IReadOnlyList<Troop> Troops => troops;
        public IReadOnlyList<Enemy> Enemies => enemies;
        public IReadOnlyList<string> Pending => pending;
        public BattleBuffControl Buffs { get; } = new BattleBuffControl();
        public IReadOnlyList<RewardKind> Rewards => rewards;
        public IReadOnlyList<StormZone> Zones => zones;
        public IReadOnlyList<WaveSpawn> Forecast => spawns ?? (spawns = BattleTables.Current.Schedule(Wave, Seed));
        public IEnumerable<CombatEvent> EventsAfter(long sequence) => events.Where(e => e.Sequence > sequence).ToArray();

        public BattleSession(int seed = 173) { Reset(seed); }

        public bool CanMerge(Troop host)
        {
            if (host == null || host.Star >= 3) return false;
            return troops.Count(t => t.Identity == host.Identity && t.Star == host.Star) >= 3;
        }

        /// <summary>
        /// 为候补单位自动寻找白色布阵框内最合适的空位（供双击上场与拖拽复用同一套落位规则）：
        /// 盾卫优先前排，远程优先中后排，优先挑选与现有单位间距宽裕的标准阵位或自由空位。
        /// </summary>
        public bool FindAutoDeployPosition(Troop unit, out float bestX, out float bestZ)
        {
            bestX = BattleDefinitions.CellX(1);
            bestZ = BattleDefinitions.CellZ(1);
            if (unit == null) return false;

            int[] preferredRows = unit.Properties.AttackRange < 2.3f ? new[] { 0, 1, 2 } : new[] { 1, 2, 0 };
            int[] preferredLanes = { 1, 0, 2 };

            float bestScore = -1f;
            bool found = false;

            foreach (int row in preferredRows)
            {
                foreach (int lane in preferredLanes)
                {
                    float cx = BattleDefinitions.CellX(row);
                    float cz = BattleDefinitions.CellZ(lane);
                    BattleDefinitions.ClampToDeployBox(cx, cz, out cx, out cz);
                    float minDist = MinDistanceToOtherDeployed(unit.Id, cx, cz);
                    if (minDist >= 0.88f)
                    {
                        bestX = cx;
                        bestZ = cz;
                        return true;
                    }
                    if (minDist > bestScore)
                    {
                        bestScore = minDist;
                        bestX = cx;
                        bestZ = cz;
                        found = true;
                    }
                }
            }

            // 若标准 9 个参考位均有近邻，则在白色布阵框内做均匀网格采样，挑选离其他单位最远的空点
            for (int ix = 0; ix < 5; ix++)
            {
                float x = BattleDefinitions.DeployMaxX - 0.35f - ix * ((BattleDefinitions.DeployMaxX - BattleDefinitions.DeployMinX - 0.7f) / 4f);
                for (int iz = 0; iz < 5; iz++)
                {
                    float z = BattleDefinitions.DeployMinZ + 0.35f + iz * ((BattleDefinitions.DeployMaxZ - BattleDefinitions.DeployMinZ - 0.7f) / 4f);
                    float minDist = MinDistanceToOtherDeployed(unit.Id, x, z);
                    if (minDist > bestScore)
                    {
                        bestScore = minDist;
                        bestX = x;
                        bestZ = z;
                        found = true;
                    }
                }
            }

            return found;
        }

        private float MinDistanceToOtherDeployed(int excludeId, float x, float z)
        {
            float minD = 99f;
            foreach (var other in troops)
            {
                if (other.Id == excludeId || !other.Deployed) continue;
                float d = Distance(other.HomeX, other.HomeZ, x, z);
                if (d < minD) minD = d;
            }
            return minD;
        }

        private void Reset(int seed)
        {
            navigation?.Reset();
            Seed = seed; random = new BattleRandom(seed); nextId = 1; Wave = 1; MarchDistance = 0f; Spins = 3;
            maxDeployed = 6; popExp = 0; lastWinRowMid = lastWinColMid = enraged = false;
            misses = spinCount = spawnCursor = 0; LastSpinSequence = 0; Phase = RunPhase.Preparation; Paused = false; Speed = 1; BattleTime = accumulator = 0;
            damageStatistics.Clear();
            slamAt = -1; slamLane = -1;
            troops.Clear(); enemies.Clear(); pending.Clear(); rewards.Clear(); zones.Clear(); events.Clear(); spawns = null;
            Buffs.Apply(troops);
            Buffs.Apply(enemies, BattleSide.Enemy);
            skillInstances.Clear(); nextSkillId = 0; resolvingSkill = null;
            Reels = new[] { TroopKind.Shield, TroopKind.Archer, TroopKind.Storm, TroopKind.Archer, TroopKind.Storm, TroopKind.Frost, TroopKind.Storm, TroopKind.Frost, TroopKind.Shield };

            // 开局不提前放置我方单位，全部通过老虎机招募获得并上阵
            Emit("run_started", "军团集结：转动十字法阵招募单位并部署上阵");
        }

        private void AddTroop(string heroId, int lane = -1, int row = -1)
        {
            var t = new Troop { Id = nextId++, HeroId = heroId, Kind = (TroopKind)BattleTables.Current.Unit(heroId).Job, Lane = lane, Row = row };
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
                    BattleDefinitions.ClampToDeployBox(
                        BattleDefinitions.CellX(t.Row),
                        BattleDefinitions.CellZ(t.Lane),
                        out float hx,
                        out float hz);
                    t.HomeX = hx;
                    t.HomeZ = hz;
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
            t.Vx = t.Vz = 0f;
            t.TargetId = 0;
            t.AiState = CombatAiState.Idle;
            t.Navigation.Reset();
            ResetUnit(t);
            t.Cooldown = .25f;
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
            if (c.Kind == CommandKind.Aim || c.Kind == CommandKind.CancelAim || c.Kind == CommandKind.Focus || c.Kind == CommandKind.Protect) return Reject("战术指挥已移除");
            if (c.Kind == CommandKind.Pause) { if (Phase != RunPhase.Fighting) return Reject("仅战斗中可暂停"); Paused = c.Flag; return new CommandResult(true, Paused ? "已暂停" : "继续战斗"); }
            if (c.Kind == CommandKind.NextWave)
            {
                if (Phase != RunPhase.WaveResult) return Reject("请先完成本波与奖励选择");
                Wave++; spawns = null; spawnCursor = 0; Phase = RunPhase.Preparation; Spins = SpinsPerWave; BattleTime = 0; Paused = false;
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
                    if (troops.Count(t => !t.Deployed) >= BattleDefinitions.ReserveCapacity) return Reject("候补区已满");
                    if (troops.Count(t => t.Deployed) <= 1) return Reject("至少保留一名上阵单位");
                    unit.Lane = unit.Row = -1;
                    unit.HomeX = unit.HomeZ = unit.X = unit.Z = 0f;
                    Emit("recalled", "已召回" + unit.Definition.Name + "至候补", unit.Id);
                    break;
                case CommandKind.Recycle:
                    if (unit.Deployed) return Reject("先拖回候补再遣散，避免误删阵线");
                    troops.Remove(unit); Emit("recycled", "已遣散候补", unit.Id);
                    break;
                case CommandKind.Merge: return Merge(unit, c.OtherId, c.ThirdId);
                default: return Reject("该指令不适用于准备阶段");
            }
            DrainPendingAndAutoMerge();
            return new CommandResult(true, "操作成功");
        }

        private CommandResult ExecuteDeploy(Troop unit, BattleCommand c)
        {
            float tx, tz;
            int targetLane, targetRow;
            if (c.UseWorldPos)
            {
                if (float.IsNaN(c.TargetX) || float.IsInfinity(c.TargetX) || float.IsNaN(c.TargetZ) || float.IsInfinity(c.TargetZ))
                {
                    return Reject("目标坐标无效");
                }
                // 超出白色布阵框不再拒绝，而是自动夹紧到可放置区域内离手最近的点 A
                BattleDefinitions.ClampToDeployBox(c.TargetX, c.TargetZ, out tx, out tz);
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

            if (!unit.Deployed && occupied == null && DeployedCount >= MaxDeployed)
                return Reject($"人口已满（{DeployedCount}/{MaxDeployed}），拖至场上单位替换或过波升级");

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

            Emit("deployed", "已部署" + unit.Definition.Name, unit.Id, 0, unit.Lane);
            DrainPendingAndAutoMerge();
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
            if (host.Star >= 3 || a.Star != host.Star || b.Star != host.Star || a.Identity != host.Identity || b.Identity != host.Identity) return Reject("需要三个同名同星单位，最高三星");
            Promote(host, a, b);
            DrainPendingAndAutoMerge();
            return new CommandResult(true, "合成成功");
        }

        private void Promote(Troop host, Troop donor, Troop secondDonor = null)
        {
            host.Star++;
            troops.Remove(donor);
            if (secondDonor != null) troops.Remove(secondDonor);
            ResetTroop(host, snapToHome: true);
            Emit("merged", $"{host.Definition.Name}升为{host.Star}星！", host.Id, 0, host.Lane, host.Star);
        }

        /// <summary>
        /// 自动三兵合成：凑齐三个同名同星（最高升至3星）时自动合成，
        /// 优先保留已上阵单位的位置作为宿主，并释放候补空间接收暂存单位。
        /// </summary>
        private void TryAutoMerge()
        {
            if (Phase != RunPhase.Preparation) return;
            bool mergedAny;
            do
            {
                mergedAny = false;
                var group = troops
                    .Where(t => t.Star < 3)
                    .GroupBy(t => new { t.Identity, t.Star })
                    .FirstOrDefault(g => g.Count() >= 3);

                if (group != null)
                {
                    var ordered = group
                        .OrderByDescending(t => t.Deployed)
                        .ThenBy(t => t.Id)
                        .ToList();
                    var host = ordered[0];
                    var donor1 = ordered[1];
                    var donor2 = ordered[2];
                    Promote(host, donor1, donor2);
                    mergedAny = true;
                }
            }
            while (mergedAny);
        }

        private void DrainPendingAndAutoMerge()
        {
            bool changed;
            do
            {
                changed = false;
                while (pending.Count > 0 && TryAcceptPending())
                {
                    pending.RemoveAt(0);
                    changed = true;
                }
                int beforeCount = troops.Count;
                TryAutoMerge();
                if (troops.Count != beforeCount) changed = true;
            }
            while (changed);
            Buffs.Apply(troops);
            foreach (var troop in troops) troop.Hp = troop.MaxHp = troop.Properties.Health;
        }

        private bool TryAcceptPending()
        {
            string hero = pending[0];
            if (troops.Count(t => !t.Deployed) < BattleDefinitions.ReserveCapacity)
            {
                AddTroop(hero);
                return true;
            }

            var pair = troops.Where(t => t.Star == 1 && t.Identity == hero)
                .OrderByDescending(t => t.Deployed).ThenBy(t => t.Id).Take(2).ToArray();
            if (pair.Length < 2) return false;
            Promote(pair[0], pair[1]);
            return true;
        }

        private CommandResult Spin()
        {
            if (Spins <= 0) return Reject("本波招募次数已用完");
            if (pending.Count > 0) return Reject("请先处理暂存单位：部署或遣散候补");
            Spins--; spinCount++;
            for (int i = 0; i < 9; i++) Reels[i] = (TroopKind)random.Next(4);
            var table = BattleTables.Current;
            // 中后期提高已有升星进度的职业与英雄权重，转数、中奖线和保底条件沿用原规则。
            var growthCandidates = Wave < table.Tune(BattleTuning.RecruitGrowthWave)
                ? Array.Empty<Troop>()
                : troops.GroupBy(t => t.Identity).Select(g => new
                    {
                        Unit = g.First(),
                        Copies = g.Sum(t => t.Star == 3 ? 9 : t.Star == 2 ? 3 : 1)
                    })
                    .Where(g => g.Copies >= 3 && g.Copies < 9)
                    .OrderByDescending(g => g.Copies).ThenBy(g => g.Unit.Id).Select(g => g.Unit).ToArray();
            var growthRandom = new BattleRandom(unchecked(Seed ^ (spinCount * 0x3f532ea5)));
            if (growthCandidates.Length > 0 && growthRandom.Next(100) < table.Tune(BattleTuning.RecruitProfessionBias))
                Reels[4] = growthCandidates[0].Kind;
            bool tutorial = spinCount <= 3;
            if (tutorial)
            {
                if (spinCount == 1) { Reels[3] = Reels[4] = Reels[5] = TroopKind.Shield; }
                else if (spinCount == 2) { Reels[1] = Reels[4] = Reels[7] = TroopKind.Archer; }
                else { Reels[3] = Reels[4] = Reels[5] = TroopKind.Storm; }
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
            var recruited = new List<string>(2);
            var recruitRandom = new BattleRandom(unchecked(Seed ^ (spinCount * 0x2c9277b5)));
            string preferred = growthCandidates.FirstOrDefault(t => t.Kind == Reels[4])?.Identity;
            if (lastWinRowMid) recruited.Add(table.Recruit(Reels[4], recruitRandom, preferred));
            if (lastWinColMid) recruited.Add(table.Recruit(Reels[4], recruitRandom, preferred));
            pending.AddRange(recruited);
            int won = recruited.Count;
            misses = won == 0 ? misses + 1 : 0;
            DrainPendingAndAutoMerge();
            string lineText = lastWinRowMid && lastWinColMid ? "十字双连！" : lastWinRowMid ? "中横三同！" : lastWinColMid ? "中竖三同！" : "";
            Emit("spin", (tutorial ? "教学预设 · " : guarantee ? "保底触发 · " : "随机招募 · ") + lineText + "出兵" + won + "名", 0, 0, -1, won);
            LastSpinSequence = eventSequence;
            return new CommandResult(true, won > 0 ? lineText + "获得" + string.Join("、", recruited.Select(id => BattleTables.Current.Unit(id).Name)) : "未连成十字三同");
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

        private void Emit(string type, string message, int source = 0, int target = 0, int lane = -1, float amount = 0)
        {
            events.Add(new CombatEvent { Sequence = ++eventSequence, Type = type, Message = message, Source = source, Target = target, Lane = lane, Amount = amount, Time = BattleTime, SkillInstanceId = resolvingSkill?.Id ?? 0, TimelineId = resolvingSkill?.TimelineId, Trigger = resolvingSkill?.Trigger, CasterX = resolvingSkill?.CasterX ?? 0f, CasterZ = resolvingSkill?.CasterZ ?? 0f, TargetX = resolvingSkill?.TargetX ?? 0f, TargetZ = resolvingSkill?.TargetZ ?? 0f, FacingYaw = resolvingSkill?.FacingYaw ?? 0f });
            if (events.Count > 600) events.RemoveRange(0, events.Count - 600);
        }

        public object NavigationSnapshot(int unitId = 0) => new
        {
            algorithm = navigation?.Backend ?? "unbound", rvoActive = navigation != null, fixedStep = BattleDefinitions.Step,
            cpuTotalMs = NavigationCpuTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
            units = troops.Where(t => t.Deployed && (unitId == 0 || t.Id == unitId)).Select(t => new
                { id = t.Id, aiState = t.AiState.ToString(), targetId = t.TargetId, x = t.X, z = t.Z, vx = t.Vx, vz = t.Vz, navigation = t.Navigation.Snapshot() }),
            enemies = enemies.Where(e => unitId == 0 || e.Id == unitId).Select(e => new
                { id = e.Id, aiState = e.AiState.ToString(), targetId = e.TargetId, x = e.X, z = e.Z, vx = e.Vx, vz = e.Vz, navigation = e.Navigation.Snapshot() })
        };

        public object Snapshot() => new
        {
            schemaVersion = 8,
            skills = skillInstances.Select(a => new { id = a.Id, source = a.Source, target = a.Target, skill = a.Skill?.Id, trigger = a.Trigger, timelineId = a.TimelineId, phase = a.Phase.ToString(), startedAt = a.StartedAt, launchAt = a.LaunchAt, impactAt = a.ImpactAt, endAt = a.EndAt, targetX = a.TargetX, targetZ = a.TargetZ, facingYaw = a.FacingYaw, cancelReason = a.CancelReason,
                timingMode = a.Definition.ProjectileSpeed > 0 ? "distance-at-cast" : "timeline", projectileSpeed = a.Definition.ProjectileSpeed }),
            layout = "horizontal-free-deploy-navmesh",
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
            teaching = spinCount < 3,
            deployedCount = DeployedCount,
            maxDeployed = MaxDeployed,
            popExp = popExp,
            popExpNeeded = PopExpNeeded,
            winRowMid = lastWinRowMid,
            winColMid = lastWinColMid,
            damageStatistics = DamageStatistics.Select(r => new { heroId = r.HeroId, star = r.Star, waveDamage = r.WaveDamage, totalDamage = r.TotalDamage }),
            paused = Paused,
            speed = Speed,
            battleTime = BattleTime,
            forecast = Forecast.Select(e => new { kind = e.MonsterId, name = BattleTables.Current.Unit(e.MonsterId).Name, batch = e.Batch, star = e.Star, lane = e.Lane, at = e.At }),
            spawned = spawnCursor,
            synergies = Buffs.Active.Select(s => new { race = s.Race?.ToString(), job = s.Job?.ToString(), threshold = s.Count, stat = s.Stat.ToString(), amount = s.Amount }),
            enemySynergies = Buffs.EnemyActive.Select(s => new { race = s.Race?.ToString(), job = s.Job?.ToString(), threshold = s.Count, stat = s.Stat.ToString(), amount = s.Amount }),
            units = troops.Select(t => new
            {
                id = t.Id,
                kind = t.Definition.Job.ToString(),
                name = t.Definition.Name,
                heroId = t.Identity,
                race = t.Definition.Race.ToString(),
                job = t.Definition.Job.ToString(),
                star = t.Star,
                lane = t.Lane,
                row = t.Row,
                homeX = Math.Round(t.HomeX, 2),
                homeZ = Math.Round(t.HomeZ, 2),
                x = Math.Round(t.X, 2),
                z = Math.Round(t.Z, 2),
                vx = t.Vx,
                vz = t.Vz,
                targetId = t.TargetId,
                aiState = t.AiState.ToString(),
                navigation = t.Navigation.Snapshot(),
                facingYaw = Math.Round(t.FacingYaw, 1),
                turnSpeed = t.TurnSpeed,
                moveSpeed = t.MoveSpeed,
                canMerge = CanMerge(t),
                hp = t.Hp,
                maxHp = t.MaxHp,
                shield = t.Shield,
                cooldown = t.Cooldown,
                combat = UnitSnapshot(t)
            }),
            enemies = enemies.Select(e => new
            {
                id = e.Id,
                kind = e.MonsterId,
                lane = e.Lane,
                x = Math.Round(e.X, 2),
                z = Math.Round(e.Z, 2),
                vx = e.Vx,
                vz = e.Vz,
                targetId = e.TargetId,
                aiState = e.AiState.ToString(),
                navigation = e.Navigation.Snapshot(),
                facingYaw = Math.Round(e.FacingYaw, 1),
                turnSpeed = e.TurnSpeed,
                moveSpeed = e.MoveSpeed,
                hp = e.Hp,
                maxHp = e.MaxHp,
                alive = e.Alive,
                grade = e.Definition.Grade.ToString(),
                star = e.Star,
                job = e.Definition.Job.ToString(),
                combat = UnitSnapshot(e),
                frozenUntil = e.FrozenUntil,
                slowUntil = e.SlowUntil
            }),
            pending = pending.Select(id => new { id, name = BattleTables.Current.Unit(id).Name }),
            rewards = rewards.Select(r => r.ToString()),
            reels = Reels.Select(k => ((UnitJob)k).ToString()),
            rewardOptions = Phase == RunPhase.Reward ? Enum.GetValues(typeof(RewardKind)).Cast<RewardKind>().Where(r => !rewards.Contains(r)).Select(r => r.ToString()).ToArray() : Array.Empty<string>(),
            bossTelegraph = new { lane = slamLane, impactAt = slamAt },
            lastEventSequence = eventSequence,
            events = events.Skip(Math.Max(0, events.Count - 35)).ToArray(),
            availableActions = AvailableActions()
        };

        private object UnitSnapshot(BattleCombatant unit) => new
        {
            mana = unit.Mana, maxMana = BattleCombatant.MaxMana, jumped = unit.Jumped,
            jumping = unit.JumpEndsAt > BattleTime, properties = unit.Properties.Snapshot(),
            statuses = unit.Status.Snapshot(), buffs = unit.Buffs.Snapshot(),
            skills = unit.Definition.Skills.Select((skill, slot) => new
            {
                slot = slot + 1, id = skill?.Id, name = skill?.Name, passive = skill?.Passive,
                unlocked = slot < unit.Star, readyIn = Math.Max(0f, unit.SkillReadyAt[slot] - BattleTime)
            })
        };

        private string[] AvailableActions()
        {
            if (Phase == RunPhase.Preparation)
            {
                var actions = new List<string> { "deploy", "restart" };
                if (Spins > 0 && pending.Count == 0) actions.Add("spin");
                if (troops.Count(t => t.Deployed) > 1 && troops.Count(t => !t.Deployed) < BattleDefinitions.ReserveCapacity) actions.Add("recall");
                if (troops.Any(t => !t.Deployed)) actions.Add("recycle");
                if (troops.GroupBy(t => new { t.Identity, t.Star }).Any(g => g.Key.Star < 3 && g.Count() >= 3)) actions.Add("merge");
                if (pending.Count == 0 && troops.Any(t => t.Deployed)) actions.Add("start_wave");
                return actions.ToArray();
            }
            if (Phase == RunPhase.Fighting) return new[] { "pause", "set_speed", "restart" };
            return Phase == RunPhase.Reward ? new[] { "choose_reward", "restart" } : Phase == RunPhase.WaveResult ? new[] { "next_wave", "restart" } : new[] { "restart" };
        }
    }
}
