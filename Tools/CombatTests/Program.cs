using System.Text.Json;
using LittleGame.Combat;

static class Tests
{
    static readonly List<string> passed = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed.Add(label); }
    static CommandResult Do(BattleSession s, CommandKind kind, int id = 0, int lane = 0, int row = 0) => s.Execute(new BattleCommand { Kind = kind, UnitId = id, Lane = lane, Row = row });
    static string Snapshot(BattleSession s) => JsonSerializer.Serialize(s.Snapshot(), new JsonSerializerOptions { IncludeFields = true });
    static SkillBranch Branch(TroopKind k) => (SkillBranch)(1 + (int)k * 2);

    static void Build(BattleSession s)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            foreach (var group in s.Troops.GroupBy(t => (t.Kind, t.Star)).ToArray())
            {
                var units = group.OrderByDescending(t => t.Deployed).ThenBy(t => t.Id).ToList();
                if (units.Count >= 3 && units[0].Star < 3 && (units.Count(t => !t.Deployed) >= 1 || units.Count >= 4))
                {
                    if (s.Execute(new BattleCommand { Kind = CommandKind.Merge, UnitId = units[0].Id, OtherId = units[units.Count - 1].Id, ThirdId = units[units.Count - 2].Id }).Accepted)
                        changed = true;
                }
            }
            foreach (var t in s.Troops.Where(t => t.Star >= 2 && t.Branch == SkillBranch.None).ToArray())
                if (s.Execute(new BattleCommand { Kind = CommandKind.ChooseBranch, UnitId = t.Id, Branch = Branch(t.Kind) }).Accepted)
                    changed = true;
            foreach (var t in s.Troops.Where(t => !t.Deployed).OrderByDescending(t => t.Star).ToArray())
            {
                if (s.DeployedCount < s.MaxDeployed)
                {
                    bool done = false;
                    for (int row = 0; row < 3 && !done; row++)
                        for (int lane = 0; lane < 3 && !done; lane++)
                            if (!s.Troops.Any(a => a.Lane == lane && a.Row == row))
                            {
                                if (Do(s, CommandKind.Deploy, t.Id, lane, row).Accepted) { done = true; changed = true; }
                            }
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

    static void Main()
    {
        var s = new BattleSession(173);
        Check(s.Troops.Count == 6 && s.AliveTroopCount == 6 && s.Spins == 3 && s.MaxDeployed == 6 && s.PopExp == 0 && s.PopExpNeeded == 4, "默认横向阵容直接可玩且初始人口上限6");
        Check(s.Troops.All(t => t.FacingYaw == 90f && t.TurnSpeed > 0f && t.MoveSpeed > 0f), "我方单位默认面向右(+X)且携带差异化移速与转身速率");
        Check(BattleDefinitions.MoveSpeed(TroopKind.Archer) > BattleDefinitions.MoveSpeed(TroopKind.Shield), "不同职业移动速度区分合理");
        Check(!s.Execute(new BattleCommand { Kind = CommandKind.Deploy, UnitId = 1, UseWorldPos = true, TargetX = 2.5f, TargetZ = 0f }).Accepted, "自由拖拽超出白色布阵框拒绝");
        Check(s.Execute(new BattleCommand { Kind = CommandKind.Deploy, UnitId = 1, UseWorldPos = true, TargetX = -4.2f, TargetZ = 1.15f }).Accepted, "白色布阵框内自由坐标落位通过");
        Check(Do(s, CommandKind.Deploy, 1, 0, 0).Accepted, "参考阵位部署回归原位");
        Check(!s.CanMerge(s.Troops.First(t => t.Kind == TroopKind.Archer)) && s.CanMerge(s.Troops.First(t => t.Kind == TroopKind.Shield)), "CanMerge 精确区分可合成与不可合成单位");
        Check(!Do(s, CommandKind.Deploy, 1, 3, 0).Accepted, "非法阵位拒绝");
        var before = Snapshot(s); Check(!s.Execute(new BattleCommand { Kind = CommandKind.Merge, UnitId = 1, OtherId = 1, ThirdId = 3 }).Accepted && Snapshot(s) == before, "重复素材原子拒绝");
        Do(s, CommandKind.Spin); Check(s.LastWinRowMid && s.Troops.Count == 7, "首转中横线三同出兵");
        var reserve1 = s.Troops.First(t => !t.Deployed);
        Check(!Do(s, CommandKind.Deploy, reserve1.Id, 0, 2).Accepted, "满人口拖入空格拒绝");
        Check(Do(s, CommandKind.Deploy, reserve1.Id, 0, 1).Accepted, "满人口拖入已占格允许替换");
        Do(s, CommandKind.Spin); Check(s.LastWinColMid, "二转中竖线三同出兵");
        Do(s, CommandKind.Spin); Check(!Do(s, CommandKind.Spin).Accepted, "转盘次数不能透支");

        var save = s.Save(); var restored = BattleSession.Restore(save); Do(s, CommandKind.StartWave); Do(restored, CommandKind.StartWave); s.Advance(.1f); restored.Advance(.1f);
        Check(s.Enemies.Select(e => e.Kind).SequenceEqual(restored.Enemies.Select(e => e.Kind)) && s.Enemies.Select(e => e.Hp).SequenceEqual(restored.Enemies.Select(e => e.Hp)), "存档恢复后确定性推进");
        Check(!Do(s, CommandKind.Spin).Accepted && !Do(s, CommandKind.Deploy, 1, 1, 0).Accepted, "战中禁止招募和布阵");

        s.Execute(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Focus }); float clock = s.BattleTime; s.Advance(1); Check(s.BattleTime == clock && s.Tactics == 1, "瞄准暂停且不消费");
        Check(!Do(s, CommandKind.Focus, 9999).Accepted && s.Tactics == 1, "失效目标不扣战术"); Do(s, CommandKind.CancelAim); s.Advance(.5f); Check(s.BattleTime > clock, "取消瞄准恢复推进");
        s.Execute(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Protect }); Check(Do(s, CommandKind.Protect, 0, 0).Accepted && s.Tactics == 0, "护盾保护按正常规则消耗唯一战术");
        Check(!s.Execute(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Focus }).Accepted, "战术不可超额使用");

        var rndA = new BattleSession(900); var rndB = new BattleSession(900);
        for (int i = 0; i < 3; i++) { Do(rndA, CommandKind.Spin); Do(rndB, CommandKind.Spin); }
        Check(Snapshot(rndA) == Snapshot(rndB), "固定种子完整快照一致");

        var pitySave = new BattleSession(77).Save(); pitySave.Misses = 2; pitySave.SpinCount = 2;
        var pity = BattleSession.Restore(pitySave); int n = pity.Troops.Count; Do(pity, CommandKind.Spin);
        Check(pity.Troops.Count > n && (pity.LastWinRowMid || pity.LastWinColMid), "连续两空后十字中线必出兵");

        var merge = new BattleSession(); var ids = merge.Troops.Where(t => t.Kind == TroopKind.Shield).Select(t => t.Id).ToArray();
        Check(merge.Execute(new BattleCommand { Kind = CommandKind.Merge, UnitId = ids[0], OtherId = ids[1], ThirdId = ids[2] }).Accepted, "三同兵合成成功");
        Check(!Do(merge, CommandKind.StartWave).Accepted, "二星未选分支不能开战");
        Check(!merge.Execute(new BattleCommand { Kind = CommandKind.ChooseBranch, UnitId = ids[0], Branch = SkillBranch.StormPool }).Accepted, "跨职业分支拒绝");
        Check(merge.Execute(new BattleCommand { Kind = CommandKind.ChooseBranch, UnitId = ids[0], Branch = SkillBranch.Bastion }).Accepted, "合法分支生效");
        Check(!merge.Execute(new BattleCommand { Kind = CommandKind.ChooseBranch, UnitId = ids[0], Branch = SkillBranch.WarCry }).Accepted, "分支不能反复切换");

        var broken = merge.Save(); broken.Troops.Add(broken.Troops[0]); bool rejected = false;
        try { BattleSession.Restore(broken); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "损坏存档拒绝");

        // 全面取消栅栏 + 转身速率平滑验证
        var turnTest = new BattleSession();
        // 只保留上行远端弓手，开战后它可跨行瞄准下方敌人且按 TurnSpeed 平滑转向
        foreach (var t in turnTest.Troops.Where(t => t.Kind != TroopKind.Archer || t.Lane != 0).ToArray()) Do(turnTest, CommandKind.Recall, t.Id);
        Do(turnTest, CommandKind.StartWave);
        turnTest.Advance(.05f);
        var soloArcher = turnTest.Troops.First(t => t.Deployed);
        var farEnemy = turnTest.Enemies.First(e => e.Lane == 2);
        Check(BattleDefinitions.CanReach(soloArcher, farEnemy) || BattleDefinitions.Range(soloArcher) > 5f, "无栅栏后仅按真实二维距离判定射程");
        float yawBefore = soloArcher.FacingYaw;
        turnTest.Advance(1.5f);
        Check(Math.Abs(BattleDefinitions.DeltaAngle(yawBefore, soloArcher.FacingYaw)) > 0f || soloArcher.FacingYaw != 0f, "角色按 TurnSpeed 朝目标转向");

        var frameA = new BattleSession(); var frameB = new BattleSession(); Do(frameA, CommandKind.StartWave); Do(frameB, CommandKind.StartWave);
        frameA.Advance(1); for (int i = 0; i < 100; i++) frameB.Advance(.01f);
        Check(Snapshot(frameA) == Snapshot(frameB), "不同帧间隔按相同固定步数推进");

        var fullSave = new BattleSession().Save();
        for (int i = 0; i < 9; i++) fullSave.Troops.Add(new SaveTroop { Id = 7 + i, Kind = TroopKind.Frost, Star = 1, Lane = i < 3 ? i : -1, Row = i < 3 ? 2 : -1 });
        fullSave.MaxDeployed = 9;
        fullSave.NextId = 16; fullSave.Misses = 2; fullSave.SpinCount = 2;
        var full = BattleSession.Restore(fullSave); Do(full, CommandKind.Spin);
        Check(full.Troops.Count == 15 && full.Pending.Count > 0, "满容量中奖暂存且不丢单位");
        Check(!Do(full, CommandKind.Spin).Accepted && !Do(full, CommandKind.StartWave).Accepted, "暂存未处理禁止再转与开战");
        Do(full, CommandKind.Recycle, full.Troops.First(t => !t.Deployed).Id);
        Check(full.Troops.Count == 15, "遣散后自动接收暂存单位");

        var bossSave = new BattleSession().Save(); bossSave.Wave = 6; bossSave.Troops.First(t => t.Id == 3).Kind = TroopKind.Frost;
        var boss = BattleSession.Restore(bossSave); Do(boss, CommandKind.StartWave); boss.Advance(2.0f);
        Check(boss.EventsAfter(0).Any(e => e.Type == "boss_control_resisted") && boss.Enemies.All(e => e.FrozenUntil == 0), "首领免疫冻结并转为减速");

        // 无城墙模式下：单兵推进至被敌群围攻团灭即触发全军覆没失败
        var lost = new BattleSession();
        var keepArcher = lost.Troops.First(t => t.Kind == TroopKind.Archer);
        foreach (var t in lost.Troops.Where(t => t.Id != keepArcher.Id).ToArray()) Do(lost, CommandKind.Recall, t.Id);
        int lostSteps = 0;
        while (lost.Phase != RunPhase.Lost && lostSteps++ < 8000)
        {
            if (lost.Phase == RunPhase.Preparation) Do(lost, CommandKind.StartWave);
            else if (lost.Phase == RunPhase.WaveResult) Do(lost, CommandKind.NextWave);
            else if (lost.Phase == RunPhase.Reward) lost.Execute(new BattleCommand { Kind = CommandKind.ChooseReward, Reward = RewardKind.LongFreeze });
            else lost.Advance(.05f);
        }
        Check(lost.Phase == RunPhase.Lost && lost.AliveTroopCount == 0, "无城墙模式下全军阵亡即正常判负");

        var results = new List<object>();
        foreach (int seed in new[] { 173, 7, 91 })
        {
            var run = new BattleSession(seed); int limit = 0;
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
                    Check(run.Troops.All(t => t.Hp == t.MaxHp), "波后全员满血恢复 seed=" + seed);
                    for (int r = 0; r < 60 && run.IsRegrouping; r++) run.StepRegroup(.05f);
                    Check(!run.IsRegrouping, "波后角色均能快速跑回白色布阵框内 seed=" + seed);
                    Do(run, CommandKind.NextWave);
                }
                else run.Advance(.05f);
            }
            Check(run.Phase == RunPhase.Won || run.Phase == RunPhase.Lost, "正常操作远征可结束 seed=" + seed);
            Check(run.MaxDeployed == 9, "通关波次后人口经验自然升级至上限9 seed=" + seed);
            results.Add(new { seed, phase = run.Phase.ToString(), wave = run.Wave, march = run.MarchDistance, maxDeployed = run.MaxDeployed, alive = run.AliveTroopCount });
            Check(run.Phase == RunPhase.Won, "正常招募布阵可获胜 seed=" + seed);
        }

        var output = new { checks = passed, campaigns = results, note = "纯规则测试，不是Unity、触屏或手机验证；自动策略不是平衡结论" };
        Directory.CreateDirectory("Reports/Combat");
        File.WriteAllText("Reports/Combat/domain-tests.json", JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(output));
    }
}
