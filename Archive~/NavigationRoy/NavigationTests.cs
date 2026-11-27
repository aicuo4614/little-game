using LittleGame.Combat;
using System.Text.Json;

static class NavigationTests
{
    static readonly List<string> checks = new();
    static readonly List<double> timings = new();
    static readonly List<long> allocations = new();
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
    static BattleNavigation.AgentInput Agent(int id, float x, float z, float radius, bool hold = false) => new()
    {
        Id = id, IsTroop = true, X = x, Z = z, Radius = radius, MaxSpeed = 2.4f,
        HoldPosition = hold, TargetId = hold ? 0 : 99, TargetX = 3.5f, TargetZ = 0,
        TargetRadius = .4f, AttackReach = 1.3f, State = new BattleNavigationState()
    };
    static void Step(BattleNavigation nav, List<BattleNavigation.AgentInput> agents, List<BattleNavigation.AgentOutput> outputs)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        nav.Step(agents, .05f, outputs);
        double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        timings.Add(elapsedMs); allocations.Add(bytes);
        for (int i = 0; i < agents.Count; i++)
        {
            var a = agents[i]; var o = outputs.Single(o => o.Id == a.Id);
            Check(Math.Abs(o.NewVx * .05f - (o.NewX - a.X)) < .00001f && Math.Abs(o.NewVz * .05f - (o.NewZ - a.Z)) < .00001f,
                "执行速度等于实际位移/步长");
            a.X = o.NewX; a.Z = o.NewZ; agents[i] = a;
        }
    }
    public static void Run()
    {
        HeapInvariant();
        var results = new List<object>();
        var outputs = new List<BattleNavigation.AgentOutput>();
        var agents = new List<BattleNavigation.AgentInput> { Agent(1, -3, 0, .4f) };
        for (int i = -1; i <= 1; i++) agents.Add(Agent(10 + i, 0, i * 1.4f, .5f, true));
        var nav = new BattleNavigation();
        float detour = 0; bool crossed = false;
        for (int i = 0; i < 240; i++)
        {
            Step(nav, agents, outputs);
            detour = Math.Max(detour, Math.Abs(agents[0].Z));
            if (agents[0].X > .8f) { crossed = true; break; }
        }
        Check(crossed && detour > 2.2f, "三个站定友军挡路：从侧边绕过而非顶人");
        Check(agents.Skip(1).All(a => a.X == 0), "绕行时站定友军不被推挤");
        results.Add(new { scenario = "friendly_wall_detour", crossed, maxAbsZ = detour, state = agents[0].State.Snapshot() });

        agents = new List<BattleNavigation.AgentInput> { Agent(1, -3, 0, .4f) };
        for (int i = -2; i <= 2; i++) agents.Add(Agent(20 + i, 0, i * 1.2f, .5f, true));
        nav = new BattleNavigation();
        for (int i = 0; i < 20; i++) Step(nav, agents, outputs);
        Check(agents[0].X == -3 && agents[0].Z == 0 && agents[0].State.Status == NavigationStatus.NoPath, "完全堵死：站定等待，不播放移动意图");
        agents.RemoveAll(a => a.Id == 20);
        bool resumed = false;
        for (int i = 0; i < 25; i++) { Step(nav, agents, outputs); if (agents[0].X > -2.9f) { resumed = true; break; } }
        Check(resumed, "阻挡移除后自动恢复寻路和移动");
        results.Add(new { scenario = "blocked_wait_then_release", resumed });

        var obstacles = new List<BattleNavObstacle>
        {
            new(0, .3f, -1.4f, 1.4f), new(-2, .3f, 1.4f, 1.7f), new(-2, .3f, -1.7f, -1.4f)
        };
        nav = new BattleNavigation(obstacles);
        agents = new List<BattleNavigation.AgentInput> { Agent(1, -1, 0, .25f) };
        float minX = -1, maxZ = 0; crossed = false;
        for (int i = 0; i < 300; i++)
        {
            Step(nav, agents, outputs); minX = Math.Min(minX, agents[0].X); maxZ = Math.Max(maxZ, Math.Abs(agents[0].Z));
            if (agents[0].X > .8f) { crossed = true; break; }
        }
        Check(crossed && minX < -2.2f && maxZ > 1.9f, "U形阻挡：先后退出开口再绕行，非局部左右试探");
        results.Add(new { scenario = "u_shape", crossed, minX, maxAbsZ = maxZ });

        nav = new BattleNavigation();
        agents = new List<BattleNavigation.AgentInput> { Agent(1, -3, -.15f, .4f), Agent(2, -3, .15f, .4f) };
        Step(nav, agents, outputs);
        Check(agents.All(a => a.State.HasGoal), "两名追击者均能预订攻击落点");
        Check(BattleNavigation.Distance(new NavPoint(agents[0].State.GoalX, agents[0].State.GoalZ), new NavPoint(agents[1].State.GoalX, agents[1].State.GoalZ)) >= .8f,
            "同目标攻击落点不重叠");
        nav = new BattleNavigation();
        var edge = Agent(1, BattleDefinitions.FieldMaxX, 0, .4f); edge.TargetX = BattleDefinitions.FieldMaxX + 1f; edge.AttackReach = .65f; edge.TargetRadius = .1f;
        agents = new List<BattleNavigation.AgentInput> { edge };
        Step(nav, agents, outputs);
        Check(outputs[0].NewVx == 0 && outputs[0].NewVz == 0, "边界无合法攻击位：零实际位移必定零速度");

        nav = new BattleNavigation();
        var left = Agent(1, -2.5f, 0, .4f); left.TargetId = 91;
        var right = Agent(2, 2.5f, 0, .4f); right.TargetId = 92; right.TargetX = -3.5f;
        agents = new List<BattleNavigation.AgentInput> { left, right };
        float minimumGap = 10; bool passedEachOther = false;
        for (int i = 0; i < 220; i++)
        {
            Step(nav, agents, outputs);
            minimumGap = Math.Min(minimumGap, BattleNavigation.Distance(new NavPoint(agents[0].X, agents[0].Z), new NavPoint(agents[1].X, agents[1].Z)));
            if (agents[0].X > 1 && agents[1].X < -1) { passedEachOther = true; break; }
        }
        Check(passedEachOther && minimumGap >= .787f, "相向行进：可错身通过且扫掠过程不互穿");
        results.Add(new { scenario = "opposing_agents", passedEachOther, minimumGap });

        obstacles = new List<BattleNavObstacle> { new(-1, 1, 1, 3), new(-1, 1, -3, -1) };
        nav = new BattleNavigation(obstacles);
        var giant = Agent(1, -3, 0, 1.45f); giant.AttackReach = 2.15f;
        agents = new List<BattleNavigation.AgentInput> { giant };
        Step(nav, agents, outputs);
        Check(agents[0].State.Status == NavigationStatus.NoPath && outputs[0].NewVx == 0, "大体型无法通过窄道时明确无路，不能套用普通兵通行图");
        nav = new BattleNavigation(obstacles);
        agents = new List<BattleNavigation.AgentInput> { Agent(1, -3, 0, .25f) };
        crossed = false;
        for (int i = 0; i < 120; i++) { Step(nav, agents, outputs); if (agents[0].X > 1.3f) { crossed = true; break; } }
        Check(crossed, "同一窄道普通体型可以通过");
        var sortedTimings = timings.OrderBy(t => t).ToArray();
        var timing = new { samples = timings.Count, p50Ms = sortedTimings[(int)(sortedTimings.Length * .50)], p95Ms = sortedTimings[(int)(sortedTimings.Length * .95)], p99Ms = sortedTimings[(int)(sortedTimings.Length * .99)], maxMs = sortedTimings.Last(), meanAllocatedBytes = allocations.Average() };
        var report = new { timing, checks = checks.Distinct().ToArray(), samplesChecked = checks.Count, scenarios = results,
            scope = "纯规则导航测试，包含真实Roy A*适配，不是Unity或手机验证" };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        try { File.WriteAllText("Reports/Combat/navigation-domain-tests.json", json); }
        catch (IOException) { File.WriteAllText($"Reports/Combat/navigation-domain-tests-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.json", json); }
        Console.WriteLine(JsonSerializer.Serialize(report));
    }

    public static void HeapInvariant()
    {
        var heap = new Roy_T.AStar.Collections.MinHeap<int>();
        foreach (int value in new[] { 1, 4, 2, 7, 5, 3, 6, 8, 9, 10 }) heap.Insert(value);
        heap.Remove(4);
        var sorted = new List<int>();
        while (heap.Count > 0) sorted.Add(heap.Extract());
        Check(sorted.SequenceEqual(new[] { 1, 2, 3, 5, 6, 7, 8, 9, 10 }), "A*优先队列删除内部节点后仍按最小代价出队");
        heap.Insert(7);
        heap.Remove(99);
        Check(heap.Count == 1 && heap.Extract() == 7, "删除不存在的节点不会清空优先队列");
    }
}
