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
        foreach (var group in s.Troops.GroupBy(t => (t.Kind, t.Star)).ToArray())
        {
            var units = group.OrderByDescending(t => t.Deployed).ThenByDescending(t => t.Star).ToList();
            if (units.Count >= 3 && units[0].Star < 3 && (units.Count(t => !t.Deployed) >= 2 || units.Count >= 5))
                s.Execute(new BattleCommand { Kind = CommandKind.Merge, UnitId = units[0].Id, OtherId = units[units.Count - 1].Id, ThirdId = units[units.Count - 2].Id });
        }
        foreach (var t in s.Troops.Where(t => t.Star >= 2 && t.Branch == SkillBranch.None).ToArray()) s.Execute(new BattleCommand { Kind = CommandKind.ChooseBranch, UnitId = t.Id, Branch = Branch(t.Kind) });
        foreach (var t in s.Troops.Where(t => !t.Deployed).OrderByDescending(t => t.Star).ToArray())
        {
            bool done = false;
            for (int row = 0; row < 3 && !done; row++) for (int lane = 0; lane < 3 && !done; lane++)
                if (!s.Troops.Any(a => a.Lane == lane && a.Row == row)) { Do(s, CommandKind.Deploy, t.Id, lane, row); done = true; }
        }
        if (s.Pending.Count > 0)
        {
            var spare = s.Troops.FirstOrDefault(t => !t.Deployed && t.Star == 1);
            if (spare != null) Do(s, CommandKind.Recycle, spare.Id);
        }
    }
    static void Main()
    {
        var s = new BattleSession(173); Check(s.Troops.Count == 6 && s.GateHp == 10 && s.Spins == 3, "默认三路阵容直接可玩");
        Check(!Do(s, CommandKind.Deploy, 1, 3, 0).Accepted, "非法阵位拒绝");
        var before = Snapshot(s); Check(!s.Execute(new BattleCommand { Kind = CommandKind.Merge, UnitId = 1, OtherId = 1, ThirdId = 3 }).Accepted && Snapshot(s) == before, "重复素材原子拒绝");
        Do(s, CommandKind.Spin); Do(s, CommandKind.Spin); Do(s, CommandKind.Spin); Check(!Do(s, CommandKind.Spin).Accepted, "转盘次数不能透支");
        var save = s.Save(); var restored = BattleSession.Restore(save); Do(s, CommandKind.StartWave); Do(restored, CommandKind.StartWave); s.Advance(.1f); restored.Advance(.1f);
        Check(s.Enemies.Select(e => e.Kind).SequenceEqual(restored.Enemies.Select(e => e.Kind)) && s.Enemies.Select(e => e.Hp).SequenceEqual(restored.Enemies.Select(e => e.Hp)), "存档恢复后确定性推进");
        Check(!Do(s, CommandKind.Spin).Accepted && !Do(s, CommandKind.Deploy, 1, 1, 0).Accepted, "战中禁止招募和布阵");
        s.Execute(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Focus }); float clock = s.BattleTime; s.Advance(1); Check(s.BattleTime == clock && s.Tactics == 1, "瞄准暂停且不消费");
        Check(!Do(s, CommandKind.Focus, 9999).Accepted && s.Tactics == 1, "失效目标不扣战术"); Do(s, CommandKind.CancelAim); s.Advance(.5f); Check(s.BattleTime > clock, "取消瞄准恢复推进");
        s.Execute(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Protect }); Check(Do(s, CommandKind.Protect, 0, 0).Accepted && s.Tactics == 0, "保护按正常规则消耗唯一战术");
        Check(!s.Execute(new BattleCommand { Kind = CommandKind.Aim, Aim = AimKind.Focus }).Accepted, "战术不可超额使用");
        var rndA = new BattleSession(900); var rndB = new BattleSession(900);
        for (int i = 0; i < 3; i++) { Do(rndA, CommandKind.Spin); Do(rndB, CommandKind.Spin); }
        Check(Snapshot(rndA) == Snapshot(rndB), "固定种子完整快照一致");
        var pitySave = new BattleSession(77).Save(); pitySave.Misses = 2; pitySave.SpinCount = 2;
        var pity = BattleSession.Restore(pitySave); int n = pity.Troops.Count; Do(pity, CommandKind.Spin); Check(pity.Troops.Count > n, "连续两空后必出兵");
        var merge = new BattleSession(); var ids = merge.Troops.Where(t => t.Kind == TroopKind.Shield).Select(t => t.Id).ToArray();
        Check(merge.Execute(new BattleCommand { Kind = CommandKind.Merge, UnitId = ids[0], OtherId = ids[1], ThirdId = ids[2] }).Accepted, "三同兵合成成功");
        Check(!Do(merge, CommandKind.StartWave).Accepted, "二星未选分支不能开战");
        Check(!merge.Execute(new BattleCommand { Kind = CommandKind.ChooseBranch, UnitId = ids[0], Branch = SkillBranch.StormPool }).Accepted, "跨职业分支拒绝");
        Check(merge.Execute(new BattleCommand { Kind = CommandKind.ChooseBranch, UnitId = ids[0], Branch = SkillBranch.Bastion }).Accepted, "合法分支生效");
        Check(!merge.Execute(new BattleCommand { Kind = CommandKind.ChooseBranch, UnitId = ids[0], Branch = SkillBranch.WarCry }).Accepted, "分支不能反复切换");
        var broken = merge.Save(); broken.Troops.Add(broken.Troops[0]); bool rejected = false; try { BattleSession.Restore(broken); } catch (ArgumentException) { rejected = true; } Check(rejected, "损坏存档拒绝");
        var laneSession = new BattleSession(); Do(laneSession, CommandKind.StartWave); laneSession.Advance(.1f);
        var archer = laneSession.Troops.First(t => t.Kind == TroopKind.Archer && t.Lane == 0); var far = laneSession.Enemies.First(e => e.Lane == 2);
        Check(!BattleDefinitions.CanReach(archer, far, false), "左路远程不能跨两路");
        var frameA = new BattleSession(); var frameB = new BattleSession(); Do(frameA, CommandKind.StartWave); Do(frameB, CommandKind.StartWave);
        frameA.Advance(1); for (int i = 0; i < 100; i++) frameB.Advance(.01f);
        Check(Snapshot(frameA) == Snapshot(frameB), "不同帧间隔按相同固定步数推进");
        var fullSave = new BattleSession().Save();
        for (int i = 0; i < 9; i++) fullSave.Troops.Add(new SaveTroop { Id = 7 + i, Kind = TroopKind.Frost, Star = 1, Lane = i < 3 ? i : -1, Row = i < 3 ? 2 : -1 });
        fullSave.NextId = 16; fullSave.Misses = 2; fullSave.SpinCount = 2;
        var full = BattleSession.Restore(fullSave); Do(full, CommandKind.Spin);
        Check(full.Troops.Count == 15 && full.Pending.Count > 0, "满容量中奖暂存且不丢单位");
        Check(!Do(full, CommandKind.Spin).Accepted && !Do(full, CommandKind.StartWave).Accepted, "暂存未处理禁止再转与开战");
        Do(full, CommandKind.Recycle, full.Troops.First(t => !t.Deployed).Id);
        Check(full.Troops.Count == 15, "遣散后自动接收暂存单位");
        var bossSave = new BattleSession().Save(); bossSave.Wave = 6; bossSave.Troops.First(t => t.Id == 3).Kind = TroopKind.Frost;
        var boss = BattleSession.Restore(bossSave); Check(!boss.FencesUp, "首领波准备即撤栏"); Do(boss, CommandKind.StartWave); boss.Advance(1.5f);
        Check(boss.EventsAfter(0).Any(e => e.Type == "boss_control_resisted") && boss.Enemies.All(e => e.FrozenUntil == 0), "首领免疫冻结并转为减速");
        var lost = new BattleSession(); foreach (var t in lost.Troops.Skip(1).ToArray()) Do(lost, CommandKind.Recall, t.Id); Do(lost, CommandKind.StartWave);
        for (int i = 0; i < 2000 && lost.Phase == RunPhase.Fighting; i++) lost.Advance(.05f);
        Check(lost.Phase != RunPhase.Fighting && lost.GateHp < 10, "漏怪会扣城门且波次正常结束");
        var results = new List<object>();
        foreach (int seed in new[] {173, 7, 91})
        {
            var run = new BattleSession(seed); int limit = 0;
            while (run.Phase != RunPhase.Won && run.Phase != RunPhase.Lost && limit++ < 30000)
            {
                if (run.Phase == RunPhase.Preparation)
                { Build(run); if (run.Spins > 0 && run.Pending.Count == 0) { Do(run, CommandKind.Spin); Build(run); } else if (run.Pending.Count == 0) Do(run, CommandKind.StartWave); }
                else if (run.Phase == RunPhase.Reward) run.Execute(new BattleCommand { Kind = CommandKind.ChooseReward, Reward = !run.Rewards.Contains(RewardKind.Reinforcements) ? RewardKind.Reinforcements : RewardKind.WideStorm });
                else if (run.Phase == RunPhase.WaveResult) Do(run, CommandKind.NextWave);
                else run.Advance(.05f);
            }
            Check(run.Phase == RunPhase.Won || run.Phase == RunPhase.Lost, "正常操作远征可结束 seed=" + seed);
            results.Add(new {seed, phase = run.Phase.ToString(), wave = run.Wave, hp = run.GateHp, lastEnemies = run.Enemies.Select(e => new { e.Kind, e.Hp, e.Z })});
            Check(run.Phase == RunPhase.Won, "正常招募布阵可获胜 seed=" + seed);
        }
        var output = new { checks = passed, campaigns = results, note = "纯规则测试，不是Unity、触屏或手机验证；自动策略不是平衡结论" };
        Directory.CreateDirectory("Reports/Combat"); File.WriteAllText("Reports/Combat/domain-tests.json", JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(output));
    }
}
