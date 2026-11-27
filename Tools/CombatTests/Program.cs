using System.Text.Json;
using LittleGame.Combat;

static class Tests
{
    static readonly List<string> passed = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed.Add(label); }
    static CommandResult Do(BattleSession s, CommandKind kind, int id = 0, int lane = 0, int row = 0) => s.Execute(new BattleCommand { Kind = kind, UnitId = id, Lane = lane, Row = row });
    static BattleSession NewSession(int seed = 173) => NavigationTests.AttachStub(new BattleSession(seed));
    static BattleSession RestoreSession(RunSave save) => NavigationTests.AttachStub(BattleSession.Restore(save));
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
        if (args.Contains("--heap")) { NavigationTests.HeapInvariant(); return; }
        if (args.Contains("--navigation")) { NavigationTests.Run(); return; }
        if (args.Contains("--movement")) { MovementTests.Run(); return; }
        var s = NewSession(173);
        Check(s.Troops.Count == 0 && s.DeployedCount == 0 && s.Spins == 3 && s.MaxDeployed == 6, "开局不提前放置我方单位，全部通过老虎机招募获得");
        Check(!Do(s, CommandKind.StartWave).Accepted, "场上零单位时禁止开战");
        Check(BattleTables.Current.Unit("Rogue").Move > BattleTables.Current.Unit("Knight").Move, "职业可以分别配置移动速度");
        Check(BattleTables.Current.Unit("Ranger").Range <= 5.0f && BattleTables.Current.Unit("Mage").Range <= 4.5f, "远程射程已收紧，避免出生即秒杀");

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

        var save = s.Save(); var restored = RestoreSession(save);
        Check(Do(s, CommandKind.StartWave).Accepted, "部署后正常开战");
        Do(restored, CommandKind.StartWave);
        s.Advance(.5f); restored.Advance(.5f);
        Check(s.Enemies.Select(e => e.MonsterId).SequenceEqual(restored.Enemies.Select(e => e.MonsterId)) && s.Enemies.Select(e => e.Hp).SequenceEqual(restored.Enemies.Select(e => e.Hp)), "存档恢复后确定性推进（纯规则桩）");
        Check(!Do(s, CommandKind.Spin).Accepted && !Do(s, CommandKind.Deploy, first.Id, 1, 0).Accepted, "战中禁止招募和布阵");

        Check(s.Forecast.Count == 2 && s.Enemies.Count == 1 && s.Enemies[0].Mana == 0f, "首波立即刷1只怪，下一批等待计时，敌人从0法力开战");
        Check(s.Forecast[1].At >= 2.2f && s.Forecast[1].At <= 3.2f && s.MaxDeployed == 6, "首波分批及随机间隔符合配表，沿用正常人口");

        Check(!Do(s, CommandKind.Aim).Accepted && !Do(s, CommandKind.Protect).Accepted, "旧战术指令拒绝");
        float clock = s.BattleTime;
        s.Execute(new BattleCommand { Kind = CommandKind.Pause, Flag = true }); s.Advance(1);
        Check(s.BattleTime == clock, "暂停停止战斗与统计");
        s.Execute(new BattleCommand { Kind = CommandKind.Pause, Flag = false });
        for (int tick = 0; tick < 1000 && s.Phase == RunPhase.Fighting; tick++) s.Advance(.05f);
        Check(s.DamageStatistics.Sum(r => r.TotalDamage) > 0, "实际扣血累计到英雄统计");
        Check(Math.Abs(s.DamageStatistics.Sum(r => r.WaveDamage) - s.EventsAfter(0).Where(e => (e.Type == "hit" || e.Type == "skill_tick") && s.Troops.Any(t => t.Id == e.Source)).Sum(e => e.Amount)) < .01f, "伤害统计等于普通攻击与技能实际扣血事件");
        var statisticsSave = s.Save(); var statisticsRestore = BattleSession.Restore(statisticsSave);
        Check(statisticsRestore.DamageStatistics.Sum(r => r.TotalDamage) == s.DamageStatistics.Sum(r => r.TotalDamage), "统计保存恢复一致");
        if (s.Phase == RunPhase.WaveResult)
        {
            Do(s, CommandKind.NextWave); Do(s, CommandKind.StartWave);
            Check(s.DamageStatistics.All(r => r.WaveDamage == 0) && s.DamageStatistics.Sum(r => r.TotalDamage) > 0, "新波清空本波并保留整局统计");
        }
        Do(s, CommandKind.NewRun);
        Check(s.DamageStatistics.Count == 0, "重开清空统计");

        var rndA = new BattleSession(900); var rndB = new BattleSession(900);
        for (int i = 0; i < 3; i++) { Do(rndA, CommandKind.Spin); Do(rndB, CommandKind.Spin); }
        Check(Snapshot(rndA) == Snapshot(rndB), "固定种子完整快照一致");

        var mergeSave = new BattleSession(55).Save();
        mergeSave.Troops.Add(new SaveTroop { Id = 1, Kind = TroopKind.Shield, Star = 1, Lane = 1, Row = 0, HomeX = BattleDefinitions.CellX(0), HomeZ = BattleDefinitions.CellZ(1), HasHomePos = true });
        mergeSave.Troops.Add(new SaveTroop { Id = 2, Kind = TroopKind.Shield, Star = 1, Lane = -1, Row = -1 });
        mergeSave.NextId = 3;
        mergeSave.SpinCount = 0;
        var mergeSession = RestoreSession(mergeSave);
        var mergeHero = BattleTables.Current.Recruit(TroopKind.Shield, new BattleRandom(unchecked(55 ^ 0x2c9277b5)));
        foreach (var staged in mergeSession.Troops) staged.HeroId = mergeHero;
        Do(mergeSession, CommandKind.Spin);
        Check(mergeSession.Troops.Count == 1 && mergeSession.Troops[0].Star == 2 && mergeSession.EventsAfter(0).Any(e => e.Type == "merged"), "凑齐3名同名英雄自动合成2星并发出merged事件");
        Check(Do(mergeSession, CommandKind.StartWave).Accepted, "升2星无需强制选分支即可直接开战");
        HeroRules(save);

        var pitySave = new BattleSession(77).Save(); pitySave.Misses = 2; pitySave.SpinCount = 3;
        var pity = RestoreSession(pitySave); Do(pity, CommandKind.Spin);
        Check(pity.LastWinRowMid || pity.LastWinColMid, "连续两空后十字中线必出兵");

        var broken = BattleSession.Restore(save).Save(); broken.Troops.Add(broken.Troops[0]); bool rejected = false;
        try { BattleSession.Restore(broken); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "损坏存档拒绝");

        var frameA = RestoreSession(save); var frameB = RestoreSession(save);
        Do(frameA, CommandKind.StartWave); Do(frameB, CommandKind.StartWave);
        frameA.Advance(1); for (int i = 0; i < 100; i++) frameB.Advance(.01f);
        Check(Snapshot(frameA) == Snapshot(frameB), "不同帧间隔按相同固定步数推进（纯规则确定性）");

        var bossSave = BattleSession.Restore(save).Save(); bossSave.Wave = 5; bossSave.Troops[0].HeroId = "Skeleton_Mage";
        var boss = RestoreSession(bossSave); Do(boss, CommandKind.StartWave);
        for (int i = 0; i < 120 && !boss.EventsAfter(0).Any(e => e.Type == "boss_control_resisted"); i++) boss.Advance(.1f);
        Check(boss.EventsAfter(0).Any(e => e.Type == "boss_control_resisted") && boss.Enemies.All(e => e.FrozenUntil == 0), "首领免疫冻结并转为减速");

        // 技能规则时序：前摇/发射/命中/后摇严格按配置推进，命中前不提前扣血
        var skillTest = RestoreSession(save);
        bool badDefRejected = false;
        var projectileDefinition = new BattleSkillDefinition { UnitKind = "Archer", Trigger = "BasicAttack", LaunchAt = .14f,
            ImpactAt = .3f, RecoveryEnd = .5f, ProjectileSpeed = 18f, ProjectileSourceY = 1f, ProjectileSourceZ = .4f,
            ProjectileTargetPoints = new Dictionary<string, float[]> { ["Grunt"] = new[] { 0f, 1f, 0f } } };
        projectileDefinition.Validate();
        float nearArrival = projectileDefinition.ResolveImpact(1f, 0f, 90f, -90f, 1f, 1f, "Grunt");
        float farArrival = projectileDefinition.ResolveImpact(6f, 0f, 90f, -90f, 1f, 1f, "Grunt");
        Check(Math.Abs(nearArrival - .29f) < .0001f, "近距离弹道遵守0.15秒最短飞行，命中不早于离弦");
        Check(farArrival > nearArrival + .1f, "远距离目标延后命中，不复用编辑器固定距离的扣血时间");
        Check(Math.Abs(farArrival - projectileDefinition.ResolveImpact(-6f, 0f, -90f, 90f, 1f, 1f, "Grunt")) < .0001f,
            "双阵营镜像距离得到一致的弹道命中时间");
        Check(projectileDefinition.ResolveImpact(6f, 0f, 90f, -90f, 1.18f, 1f, "Grunt") < farArrival,
            "升星体型增大同步修正发射挂点和飞行距离");
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
            firstCast = skillTest.EventsAfter(0).FirstOrDefault(e => e.Type == "cast_started" && e.SkillInstanceId > 0 && e.Trigger == "BasicAttack");
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

        var lost = NewSession(173);
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
            var run = NewSession(seed); int limit = 0;
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
            results.Add(new { seed, phase = run.Phase.ToString(), wave = run.Wave, wave1Duration = Math.Round(wave1Duration, 2), march = run.MarchDistance, maxDeployed = run.MaxDeployed, alive = run.AliveTroopCount, threeStars = run.Troops.Count(t => t.Star == 3) });
            Check(run.Phase == RunPhase.Won, "从零单位招募起手正常布阵可打通10波 seed=" + seed);
        }

        var output = new { checks = passed, campaigns = results, note = "纯规则测试（使用测试位移桩，不替代Unity原生NavMesh验证）；自动策略不是平衡结论" };
        Directory.CreateDirectory("Reports/Combat");
        string reportPath = args.Contains("--movement-report") ? "Reports/Combat/domain-tests-movement.json" : "Reports/Combat/domain-tests.json";
        if (args.Contains("--hero-rework"))
        {
            Directory.CreateDirectory("Reports/Combat/hero-rework-20261127");
            reportPath = "Reports/Combat/hero-rework-20261127/domain-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".json";
        }
        string reportJson = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
        try { File.WriteAllText(reportPath, reportJson); }
        catch (IOException) { File.WriteAllText(reportPath.Replace(".json", "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".json"), reportJson); }
        Console.WriteLine(JsonSerializer.Serialize(output));
    }

    static void HeroRules(RunSave checkpoint)
    {
        var stage = BattleTables.Current;
        Check(stage.TotalWaves == 10 && Enumerable.Range(1, 10).Where(stage.IsBoss).SequenceEqual(new[] { 5, 10 }) &&
            stage.Schedule(5, 173).Count == 1 && stage.Schedule(10, 173).Count == 1, "两场 Boss 波各只有一只石甲巨像");
        var status = new UnitStatusControl();
        status.Apply(UnitStatus.Frozen, 1, 1f);
        status.Apply(UnitStatus.Frozen, 2, 2f);
        status.Apply(UnitStatus.Silenced, 3, 3f);
        status.Tick(1.2f);
        Check(!status.CanJump(1.2f), "一个冻结来源到期时保留其他冻结");
        status.Tick(2.2f);
        Check(status.CanAttack(2.2f) && !status.CanCast(2.2f), "沉默只阻止施法，控制可独立结束");

        var save = BattleSession.Restore(checkpoint).Save();
        save.Troops[0].HeroId = "Knight";
        save.Troops[1].HeroId = "Knight";
        save.Troops[2].HeroId = "Skeleton_Warrior";
        var synergy = RestoreSession(save);
        Check(!synergy.CanMerge(synergy.Troops[0]) && synergy.Buffs.Active.Count == 1 &&
            synergy.Buffs.Active[0].Job == UnitJob.Warrior, "同职业不同名不能合成，重复英雄不重复计羁绊");

        save.Troops.RemoveRange(1, save.Troops.Count - 1);
        save.Troops[0].Star = 3;
        var match = RestoreSession(save);
        Check(Do(match, CommandKind.StartWave).Accepted, "三星测试正常进入战斗");
        var hero = match.Troops[0];
        var monster = match.Enemies[0];
        hero.X = hero.Z = monster.Z = 0f;
        monster.X = 1.08f;
        monster.Hp = 9999f;
        monster.Cooldown = 0f;
        hero.Cooldown = 0f;
        match.Advance(.05f);
        match.Advance(.3f);
        Check(hero.Mana > 0f && monster.Mana > 0f, "敌我普攻命中和受到伤害均回蓝");
        monster.Cooldown = 100f;
        match.Advance(.5f);
        hero.Mana = BattleCombatant.MaxMana;
        match.Advance(.05f);
        Check(hero.Mana == 0f && match.SkillInstances.Last(c => c.Source == hero.Id).Skill.Id == "Rally", "满蓝优先释放技能三并消耗100法力");
        hero.Cooldown = 100f;
        match.Advance(.6f);
        hero.Mana = BattleCombatant.MaxMana;
        match.Advance(.05f);
        Check(hero.Mana == 0f && match.SkillInstances.Last(c => c.Source == hero.Id).Skill.Id == "ShieldStrike", "技能三冷却时跳过被动技能二并释放技能一");
    }
}
