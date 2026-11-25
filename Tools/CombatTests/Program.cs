using System.Text.Json;
using LittleGame.Combat;

static class Tests
{
    static readonly List<string> passed = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed.Add(label); }
    static CommandResult Do(BattleSession s, CommandKind kind, int id = 0, int lane = 0, int row = 0) => s.Execute(new BattleCommand { Kind = kind, UnitId = id, Lane = lane, Row = row });
    static string Snapshot(BattleSession s) => JsonSerializer.Serialize(s.Snapshot(), new JsonSerializerOptions { IncludeFields = true });

    static void Build(BattleSession s)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            foreach (var t in s.Troops.Where(t => !t.Deployed).OrderByDescending(t => t.Star).ToArray())
            {
                if (s.DeployedCount < s.MaxDeployed)
                {
                    if (s.FindAutoDeployPosition(t, out float ax, out float az)
                        && s.Execute(new BattleCommand { Kind = CommandKind.Deploy, UnitId = t.Id, UseWorldPos = true, TargetX = ax, TargetZ = az }).Accepted)
                        changed = true;
                }
                else
                {
                    var weakest = s.Troops.Where(a => a.Deployed && a.Star < t.Star).OrderBy(a => a.Star).FirstOrDefault();
                    if (weakest != null && Do(s, CommandKind.Deploy, t.Id, weakest.Lane, weakest.Row).Accepted)
                        changed = true;
                }
            }
            if (s.Pending.Count > 0)
            {
                var spare = s.Troops.FirstOrDefault(t => !t.Deployed && t.Star == 1) ?? s.Troops.FirstOrDefault(t => !t.Deployed);
                if (spare != null && Do(s, CommandKind.Recycle, spare.Id).Accepted) changed = true;
            }
            if (!changed) break;
        }
    }

    static void Main(string[] args)
    {
        if (args.Contains("--navigation")) { NavigationTests.Run(); return; }
        if (args.Contains("--movement")) { MovementTests.Run(); return; }
        var s = new BattleSession(173);
        Check(s.Troops.Count == 0 && s.DeployedCount == 0 && s.Spins == 3 && s.MaxDeployed == 6, "开局不提前放置我方单位，全部通过老虎机招募获得");
        Check(!Do(s, CommandKind.StartWave).Accepted, "场上零单位时禁止开战");
        Check(BattleDefinitions.MoveSpeed(TroopKind.Archer) == BattleDefinitions.MoveSpeed(TroopKind.Shield)
            && BattleDefinitions.MoveSpeed(TroopKind.Storm) == BattleDefinitions.FixedTroopMoveSpeed, "我方角色移动速度统一固定");
        Check(BattleDefinitions.Range(TroopKind.Archer) <= 5.0f && BattleDefinitions.Range(TroopKind.Storm) <= 4.5f, "远程射程已收紧，避免出生即秒杀");

        Do(s, CommandKind.Spin);
        Check(s.LastWinRowMid && s.Troops.Count == 1 && !s.Troops[0].Deployed && s.Troops[0].Kind == TroopKind.Shield, "首转中横三同盾卫正确进入候补");
        Do(s, CommandKind.Spin);
        Check(s.LastWinColMid && s.Troops.Count == 2 && s.Troops[1].Kind == TroopKind.Archer, "二转中竖三同弓手正确进入候补");
        Do(s, CommandKind.Spin);
        Check(s.LastWinRowMid && s.Troops.Count == 3 && s.Troops[2].Kind == TroopKind.Storm, "三转中横三同雷法正确进入候补");
        Check(!Do(s, CommandKind.Spin).Accepted, "转盘次数不能透支");

        var first = s.Troops[0];
        Check(s.FindAutoDeployPosition(first, out float ax, out float az) && BattleDefinitions.IsInsideDeployBox(ax, az), "自动上场找到白框内合法空位");
        Check(s.Execute(new BattleCommand { Kind = CommandKind.Deploy, UnitId = first.Id, UseWorldPos = true, TargetX = ax, TargetZ = az }).Accepted, "双击自动上场复用Deploy规则成功");
        Check(s.Execute(new BattleCommand { Kind = CommandKind.Deploy, UnitId = first.Id, UseWorldPos = true, TargetX = 2.5f, TargetZ = 4.5f }).Accepted, "拖出白色布阵框吸附最近合法点A");
        Check(Math.Abs(first.HomeX - BattleDefinitions.DeployMaxX) < 0.05f && Math.Abs(first.HomeZ - BattleDefinitions.DeployMaxZ) < 0.05f, "吸附落点A精确位于白色布阵框最近边缘");
        Check(Do(s, CommandKind.Deploy, first.Id, 1, 0).Accepted, "参考阵位部署回归原位");
        Check(!Do(s, CommandKind.Deploy, first.Id, 3, 0).Accepted, "非法参考阵位拒绝");

        Build(s);
        Check(s.DeployedCount == 3, "全部起手单位成功上阵");

        var save = s.Save(); var restored = BattleSession.Restore(save);
        Check(Do(s, CommandKind.StartWave).Accepted, "部署后正常开战");
        Do(restored, CommandKind.StartWave);
        s.Advance(.5f); restored.Advance(.5f);
        Check(s.Enemies.Select(e => e.Kind).SequenceEqual(restored.Enemies.Select(e => e.Kind)) && s.Enemies.Select(e => e.Hp).SequenceEqual(restored.Enemies.Select(e => e.Hp)), "存档恢复后确定性推进(含RVO2)");
        Check(!Do(s, CommandKind.Spin).Accepted && !Do(s, CommandKind.Deploy, first.Id, 1, 0).Accepted, "战中禁止招募和布阵");

        while (s.Phase == RunPhase.Fighting && s.BattleTime < 2.5f) s.Advance(.05f);
        Check(s.Phase == RunPhase.Fighting && s.Enemies.Any(e => e.Alive), "第1波怪物2.5秒内不会被秒杀，具备正常接敌过渡");

        s.Execute(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Focus }); float clock = s.BattleTime; s.Advance(1); Check(s.BattleTime == clock && s.Tactics == 1, "瞄准暂停且不消费");
        Check(!Do(s, CommandKind.Focus, 9999).Accepted && s.Tactics == 1, "失效目标不扣战术"); Do(s, CommandKind.CancelAim); s.Advance(.5f); Check(s.BattleTime > clock, "取消瞄准恢复推进");
        s.Execute(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Protect }); Check(Do(s, CommandKind.Protect, 0, 1).Accepted && s.Tactics == 0, "护盾保护按正常规则消耗唯一战术");
        Check(!s.Execute(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Focus }).Accepted, "战术不可超额使用");

        var rndA = new BattleSession(900); var rndB = new BattleSession(900);
        for (int i = 0; i < 3; i++) { Do(rndA, CommandKind.Spin); Do(rndB, CommandKind.Spin); }
        Check(Snapshot(rndA) == Snapshot(rndB), "固定种子完整快照一致");

        var mergeSave = new BattleSession(55).Save();
        mergeSave.Troops.Add(new SaveTroop { Id = 1, Kind = TroopKind.Shield, Star = 1, Lane = 1, Row = 0, HomeX = BattleDefinitions.CellX(0), HomeZ = BattleDefinitions.CellZ(1), HasHomePos = true });
        mergeSave.Troops.Add(new SaveTroop { Id = 2, Kind = TroopKind.Shield, Star = 1, Lane = -1, Row = -1 });
        mergeSave.NextId = 3;
        mergeSave.SpinCount = 0;
        var mergeSession = BattleSession.Restore(mergeSave);
        Do(mergeSession, CommandKind.Spin);
        Check(mergeSession.Troops.Count == 1 && mergeSession.Troops[0].Star == 2 && mergeSession.EventsAfter(0).Any(e => e.Type == "merged"), "凑齐3同职业自动合成2星并发出merged事件");
        Check(Do(mergeSession, CommandKind.StartWave).Accepted, "升2星无需强制选分支即可直接开战");

        var pitySave = new BattleSession(77).Save(); pitySave.Misses = 2; pitySave.SpinCount = 3;
        var pity = BattleSession.Restore(pitySave); Do(pity, CommandKind.Spin);
        Check(pity.LastWinRowMid || pity.LastWinColMid, "连续两空后十字中线必出兵");

        var broken = BattleSession.Restore(save).Save(); broken.Troops.Add(broken.Troops[0]); bool rejected = false;
        try { BattleSession.Restore(broken); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "损坏存档拒绝");

        var frameA = BattleSession.Restore(save); var frameB = BattleSession.Restore(save);
        Do(frameA, CommandKind.StartWave); Do(frameB, CommandKind.StartWave);
        frameA.Advance(1); for (int i = 0; i < 100; i++) frameB.Advance(.01f);
        Check(Snapshot(frameA) == Snapshot(frameB), "不同帧间隔按相同固定步数推进(RVO2确定性)");

        var bossSave = BattleSession.Restore(save).Save(); bossSave.Wave = 6; bossSave.Troops[0].Kind = TroopKind.Frost;
        var boss = BattleSession.Restore(bossSave); Do(boss, CommandKind.StartWave);
        for (int i = 0; i < 120 && !boss.EventsAfter(0).Any(e => e.Type == "boss_control_resisted"); i++) boss.Advance(.1f);
        Check(boss.EventsAfter(0).Any(e => e.Type == "boss_control_resisted") && boss.Enemies.All(e => e.FrozenUntil == 0), "首领免疫冻结并转为减速");

        // 技能规则时序：前摇/发射/命中/后摇严格按配置推进，命中前不提前扣血
        var skillTest = BattleSession.Restore(save);
        bool badDefRejected = false;
        try { skillTest.ConfigureSkills(new[] { new BattleSkillDefinition { UnitKind = "Archer", Trigger = "BasicAttack", LaunchAt = 0.4f, ImpactAt = 0.2f, RecoveryEnd = 0.5f } }); }
        catch (ArgumentException) { badDefRejected = true; }
        Check(badDefRejected, "非法技能时序(命中早于发射)被规则层拒绝");

        skillTest.ConfigureSkills(new[] {
            new BattleSkillDefinition { UnitKind = "*", Trigger = "BasicAttack", TimelineId = "test_basic", LaunchAt = 0.20f, ImpactAt = 0.50f, RecoveryEnd = 0.70f },
            new BattleSkillDefinition { UnitKind = "*", Trigger = "Skill", TimelineId = "test_skill", LaunchAt = 0.25f, ImpactAt = 0.60f, RecoveryEnd = 0.85f }
        });
        Do(skillTest, CommandKind.StartWave);
        CombatEvent firstCast = null;
        for (int i = 0; i < 200 && firstCast == null; i++)
        {
            skillTest.Advance(.05f);
            firstCast = skillTest.EventsAfter(0).FirstOrDefault(e => e.Type == "cast_started" && e.SkillInstanceId > 0);
        }
        Check(firstCast != null, "开战后技能通过 cast_started 进入规则层实例调度");
        int castId = firstCast.SkillInstanceId;
        var inst0 = skillTest.SkillInstances.First(a => a.Id == castId);
        Check(!inst0.Launched && !inst0.Impacted && inst0.Phase == BattleSkillPhase.Windup, "起手瞬间处于前摇(Windup)，尚未发射与命中");
        Check(!skillTest.EventsAfter(0).Any(e => e.SkillInstanceId == castId && (e.Type == "hit" || e.Type == "cast_impact")), "命中节点前不会提前扣血");
        for (int i = 0; i < 25; i++) skillTest.Advance(.05f);
        var castEvents = skillTest.EventsAfter(0).Where(e => e.SkillInstanceId == castId).ToList();
        var evStart = castEvents.FirstOrDefault(e => e.Type == "cast_started");
        var evLaunch = castEvents.FirstOrDefault(e => e.Type == "cast_launched");
        var evImpact = castEvents.FirstOrDefault(e => e.Type == "cast_impact");
        var evComplete = castEvents.FirstOrDefault(e => e.Type == "cast_completed");
        var evCancel = castEvents.FirstOrDefault(e => e.Type == "cast_cancelled");
        Check(evLaunch != null && evLaunch.Time + 0.001f >= evStart.Time + 0.20f, "发射事件按 LaunchAt 精确触发");
        if (evCancel == null)
        {
            Check(evImpact != null && evImpact.Time + 0.001f >= evStart.Time + 0.50f, "命中事件按 ImpactAt 精确触发，不提前结算");
            Check(evComplete != null && evComplete.Time + 0.001f >= evStart.Time + 0.70f, "后摇结束事件按 RecoveryEnd 触发");
        }

        var lost = new BattleSession(173);
        Do(lost, CommandKind.Spin);
        Do(lost, CommandKind.Spin);
        var soloArcher = lost.Troops.First(t => t.Kind == TroopKind.Archer);
        Do(lost, CommandKind.Deploy, soloArcher.Id, 1, 1);
        int lostSteps = 0;
        while (lost.Phase != RunPhase.Lost && lostSteps++ < 8000)
        {
            if (lost.Phase == RunPhase.Preparation) Do(lost, CommandKind.StartWave);
            else if (lost.Phase == RunPhase.WaveResult) Do(lost, CommandKind.NextWave);
            else if (lost.Phase == RunPhase.Reward) lost.Execute(new BattleCommand { Kind = CommandKind.ChooseReward, Reward = RewardKind.LongFreeze });
            else lost.Advance(.05f);
        }
        Check(lost.Phase == RunPhase.Lost && lost.AliveTroopCount == 0, "无城墙模式下单兵阵亡即正常判负");

        var results = new List<object>();
        foreach (int seed in new[] { 173, 7, 91 })
        {
            var run = new BattleSession(seed); int limit = 0;
            float wave1Duration = 0f;
            while (run.Phase != RunPhase.Won && run.Phase != RunPhase.Lost && limit++ < 30000)
            {
                if (run.Phase == RunPhase.Preparation)
                {
                    Build(run);
                    if (run.Spins > 0 && run.Pending.Count == 0) { Do(run, CommandKind.Spin); Build(run); }
                    else if (run.Pending.Count == 0) Do(run, CommandKind.StartWave);
                }
                else if (run.Phase == RunPhase.Reward) run.Execute(new BattleCommand { Kind = CommandKind.ChooseReward, Reward = !run.Rewards.Contains(RewardKind.Reinforcements) ? RewardKind.Reinforcements : RewardKind.WideStorm });
                else if (run.Phase == RunPhase.WaveResult)
                {
                    if (run.Wave == 1) wave1Duration = run.BattleTime;
                    Check(run.Troops.All(t => t.Hp == t.MaxHp), "波后全员满血恢复 seed=" + seed);
                    for (int r = 0; r < 60 && run.IsRegrouping; r++) run.StepRegroup(.05f);
                    Check(!run.IsRegrouping, "波后角色均能快速跑回白色布阵框内 seed=" + seed);
                    Do(run, CommandKind.NextWave);
                }
                else run.Advance(.05f);
            }
            Check(run.Phase == RunPhase.Won || run.Phase == RunPhase.Lost, "正常操作远征可结束 seed=" + seed);
            Check(run.MaxDeployed == 9, "通关波次后人口经验自然升级至上限9 seed=" + seed);
            results.Add(new { seed, phase = run.Phase.ToString(), wave = run.Wave, wave1Duration = Math.Round(wave1Duration, 2), march = run.MarchDistance, maxDeployed = run.MaxDeployed, alive = run.AliveTroopCount });
            Check(run.Phase == RunPhase.Won, "从零单位招募起手正常布阵可打通12波 seed=" + seed);
        }

        var output = new { checks = passed, campaigns = results, note = "纯规则测试，不是Unity、触屏或手机验证；自动策略不是平衡结论" };
        Directory.CreateDirectory("Reports/Combat");
        string reportPath = args.Contains("--movement-report") ? "Reports/Combat/domain-tests-movement.json" : "Reports/Combat/domain-tests.json";
        string reportJson = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
        try { File.WriteAllText(reportPath, reportJson); }
        catch (IOException) { File.WriteAllText(reportPath.Replace(".json", "-latest.json"), reportJson); }
        Console.WriteLine(JsonSerializer.Serialize(output));
    }
}
