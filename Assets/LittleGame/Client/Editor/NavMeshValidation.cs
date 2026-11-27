using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LittleGame.Combat;
using LittleGame.Client.Battle;
using Newtonsoft.Json;
using Unity.Profiling;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LittleGame.Client.Editor
{
    /// <summary>隔离原生导航几何/状态/压力测试；不改真实存档与战斗数值，不能代替正常十波验收。</summary>
    public static class NavMeshValidation
    {
        private const string Output = "Reports/Combat/navmesh-20261127/validation-latest.json";
        private static readonly List<object> results = new List<object>();
        private static string current;
        private static int testIndex;
        private static bool running;

        private static BattleNavigation.AgentInput Agent(int id, float x, float z, float radius, bool hold = false) => new BattleNavigation.AgentInput
        {
            Id = id, IsTroop = true, X = x, Z = z, Radius = radius, MaxSpeed = 2.4f, TargetId = hold ? 0 : 99,
            TargetX = 3.5f, TargetZ = 0, TargetRadius = .4f, AttackReach = 1.3f, HoldPosition = hold, State = new BattleNavigationState()
        };

        [MenuItem("Little Game/Combat/Verify Native Navigation")]
        public static async void Run()
        {
            if (!Application.isPlaying || running) throw new InvalidOperationException("需要Play且未在验证。");
            running = true; results.Clear(); testIndex = 0; current = "init"; Write("running", null);
            try
            {
                // 1. 站定墙绕行 + 站定单位不被推移
                current = "stationary_wall_detour";
                var units = new List<BattleNavigation.AgentInput> { Agent(1, -3, 0, .4f) };
                for (int i = -1; i <= 1; i++) units.Add(Agent(10 + i, 0, i * 1.4f, .5f, true));
                float detour = 0;
                using (var nav = Create())
                {
                    await Simulate(nav, units, 7, () => detour = Mathf.Max(detour, Mathf.Abs(units[0].Z)),
                        () => units[0].X > 1.2f && detour > 2.1f);
                    Require(units[0].X > .8f && detour > 2.1f, $"绕过站定墙 x={units[0].X:F2} detour={detour:F2}");
                    Require(units[1].X == 0 && units[2].X == 0 && units[3].X == 0, "站定单位不被推移");
                    Result(new { scenario = current, passed = true, x = units[0].X, maxAbsZ = detour });
                }

                // 2. 完全封死后站定等待 + 撤销中间阻挡后恢复移动
                current = "blocked_then_release";
                units = new List<BattleNavigation.AgentInput> { Agent(1, -3, 0, .4f) };
                for (int i = -2; i <= 2; i++) units.Add(Agent(20 + i, 0, i * 1.2f, .5f, true));
                using (var nav = Create())
                {
                    await Simulate(nav, units, 2.2f);
                    float x = units[0].X, z = units[0].Z;
                    await Simulate(nav, units, .6f);
                    Require(Mathf.Abs(units[0].X - x) < .05f && Mathf.Abs(units[0].Z - z) < .05f,
                        $"封死后站定等待 before=({x:F2},{z:F2}) after=({units[0].X:F2},{units[0].Z:F2})");
                    Require(units[0].State.Status == NavigationStatus.NoPath || units[0].State.Status == NavigationStatus.Waiting,
                        "封死后进入等待/无路状态 status=" + units[0].State.Status);
                    units.RemoveAll(a => a.Id == 20);
                    await Simulate(nav, units, 4.5f, stopWhen: () => units[0].X > 1.0f);
                    Require(units[0].X > .8f, "撤销阻挡后恢复 x=" + units[0].X + " z=" + units[0].Z + " nav=" + JsonConvert.SerializeObject(nav.Snapshot()));
                    Result(new { scenario = current, passed = true, waitingX = x, finalX = units[0].X, blockReason = units[0].State.BlockReason });
                }

                // 3. U形阻挡全局绕行
                current = "u_shape_detour";
                var uObstacles = new List<BattleNavObstacleData>
                {
                    new BattleNavObstacleData(-.2f, .2f, -1.6f, 1.6f),
                    new BattleNavObstacleData(-2.0f, .2f, 1.3f, 1.6f),
                    new BattleNavObstacleData(-2.0f, .2f, -1.6f, -1.3f)
                };
                using (var nav = Create(uObstacles))
                {
                    units = new List<BattleNavigation.AgentInput> { Agent(1, -1.2f, 0, .4f) };
                    float maxAbsZ = 0f;
                    await Simulate(nav, units, 7f, () => maxAbsZ = Mathf.Max(maxAbsZ, Mathf.Abs(units[0].Z)),
                        () => units[0].X > 1.2f && maxAbsZ > 1.8f);
                    Require(units[0].X > .8f && maxAbsZ > 1.8f, $"U形障碍绕出并抵达 x={units[0].X:F2} maxAbsZ={maxAbsZ:F2}");
                    Result(new { scenario = current, passed = true, finalX = units[0].X, maxAbsZ });
                }

                // 4. 相向错身通过且不互穿
                current = "opposing";
                units = new List<BattleNavigation.AgentInput> { Agent(1, -3, -.15f, .4f), Agent(2, 3, .15f, .4f) };
                var right = units[1]; right.TargetId = 98; right.TargetX = -3.5f; units[1] = right;
                float gap = float.MaxValue;
                using (var nav = Create())
                {
                    await Simulate(nav, units, 5, () => gap = Mathf.Min(gap, Distance(units[0], units[1])),
                        () => units[0].X > 1.2f && units[1].X < -1.2f);
                    Require(units[0].X > 1 && units[1].X < -1, $"相向错身通过 x0={units[0].X:F2} x1={units[1].X:F2}");
                    Require(gap >= .74f, $"相向不互穿 gap={gap:F3}");
                    Result(new { scenario = current, passed = true, minimumGap = gap });
                }

                // 5. 冻结绝对站定、解冻恢复（含切洞后重绑定）、暂停与继续
                current = "freeze_pause_resume";
                units = new List<BattleNavigation.AgentInput> { Agent(1, -3, 0, .4f) };
                using (var nav = Create())
                {
                    await Simulate(nav, units, .7f);
                    var unit = units[0]; unit.HoldPosition = true; units[0] = unit;
                    float held = unit.X; await Simulate(nav, units, .95f);
                    Require(Mathf.Abs(units[0].X - held) < .001f, "冻结绝对站定");
                    unit = units[0]; unit.HoldPosition = false; units[0] = unit;
                    await Simulate(nav, units, 1.2f, stopWhen: () => units[0].X > held + .45f);
                    Require(units[0].X > held + .2f, $"解冻恢复 held={held:F2} now={units[0].X:F2}");
                    float before = units[0].X;
                    nav.PrepareFrame(true, true, 2); await NextFrame(); await Task.Delay(150);
                    Require(units[0].X == before, "暂停不写规则位移");
                    await Simulate(nav, units, .5f);
                    Require(units[0].X >= before, "暂停继续衔接");
                    Result(new { scenario = current, passed = true, heldX = held, resumedX = units[0].X });
                }

                // 6. 不同体型窄道区分
                var obstacles = new List<BattleNavObstacleData>
                {
                    new BattleNavObstacleData(0, .3f, -4, -.5f),
                    new BattleNavObstacleData(0, .3f, .5f, 4)
                };
                current = "body_size";
                using (var nav = Create(obstacles))
                {
                    var giant = Agent(1, -3, 0, 1.15f); giant.AttackReach = 1.6f; giant.TargetRadius = .1f;
                    units = new List<BattleNavigation.AgentInput> { giant };
                    await Simulate(nav, units, 1.6f);
                    Require(units[0].X < 0, $"首领不能走过窄道 x={units[0].X:F2}");
                    nav.Reset(); units = new List<BattleNavigation.AgentInput> { Agent(1, -3, 0, .25f) };
                    await Simulate(nav, units, 4, stopWhen: () => units[0].X > 1.0f);
                    Require(units[0].X > .7f, $"普通体型能过同一窄道 x={units[0].X:F2}");
                    Result(new { scenario = current, passed = true });
                }

                // 7. 六近战围攻 Boss
                current = "boss_surround";
                units = new List<BattleNavigation.AgentInput>();
                for (int i = 0; i < 6; i++)
                {
                    var a = Agent(i + 1, -3 - i / 3, i % 3 * 1.6f - 1.6f, .5f);
                    a.AttackReach = 2.05f; a.TargetRadius = 1.15f; units.Add(a);
                }
                var boss = Agent(99, 3.5f, 0, 1.15f, true); boss.IsTroop = false; units.Add(boss);
                using (var nav = Create())
                {
                    await Simulate(nav, units, 7, () =>
                    {
                        for (int i = 0; i < 6; i++)
                            if (Distance(units[i], units[6]) < 2.01f) { var a = units[i]; a.HoldPosition = true; units[i] = a; }
                    }, () => Enumerable.Range(0, 6).All(i => units[i].HoldPosition));
                    for (int i = 0; i < 6; i++) Require(Distance(units[i], units[6]) <= 2.08f, $"围攻单位抵达射程 {i} dist={Distance(units[i], units[6]):F2}");
                    Result(new { scenario = current, passed = true, agents = 7 });
                }

                // 8. 攻击位占满后额外近战明确等待，不挤开已站定单位
                current = "attack_slots_full";
                units = new List<BattleNavigation.AgentInput>();
                var targetUnit = Agent(99, 1.5f, 0, .4f, true); targetUnit.IsTroop = false; units.Add(targetUnit);
                var ringInitial = new List<Vector2>();
                for (int i = 0; i < 6; i++)
                {
                    float ang = i * Mathf.PI / 3f;
                    float sx = 1.5f + Mathf.Cos(ang) * 0.92f;
                    float sz = Mathf.Sin(ang) * 0.92f;
                    var holder = Agent(10 + i, sx, sz, .45f, true);
                    units.Add(holder);
                    ringInitial.Add(new Vector2(sx, sz));
                }
                var extra = Agent(50, -3.2f, 0, .45f); extra.AttackReach = 1.0f; extra.TargetX = 1.5f; extra.TargetZ = 0f; extra.TargetRadius = .4f;
                units.Add(extra);
                using (var nav = Create())
                {
                    await Simulate(nav, units, 2.0f);
                    for (int i = 0; i < 6; i++)
                        Require(Vector2.Distance(new Vector2(units[i + 1].X, units[i + 1].Z), ringInitial[i]) < .001f, $"站定围攻单位不被挤动 {i}");
                    var extraState = units[units.Count - 1].State;
                    Require(extraState.Status == NavigationStatus.Waiting || extraState.Status == NavigationStatus.NoPath,
                        "攻击位满时额外近战等待 status=" + extraState.Status + " reason=" + extraState.BlockReason);
                    Result(new { scenario = current, passed = true, extraStatus = extraState.Status.ToString(), reason = extraState.BlockReason });
                }

                // 9. 18 单位实际规模 & 50 单位压力性能基准（隔离场景，Editor 无限帧率）
                current = "benchmark_18_and_50";
                var bench18 = await RunStressBenchmark(18, 2.5f);
                var bench50 = await RunStressBenchmark(50, 2.5f);
                Result(new { scenario = current, passed = true, scale18 = bench18, scale50 = bench50 });

                Write("passed", null);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Write("failed", error.ToString());
            }
            finally { running = false; }
        }

        private static async Task<object> RunStressBenchmark(int count, float duration)
        {
            int half = count / 2;
            var units = new List<BattleNavigation.AgentInput>(count);
            for (int i = 0; i < half; i++)
            {
                float x = -4.6f + (i / 5) * 0.95f;
                float z = -2.2f + (i % 5) * 1.1f;
                var a = Agent(i + 1, x, z, .38f);
                a.TargetId = half + 1 + (i % (count - half));
                a.TargetX = 4.6f - ((i % (count - half)) / 5) * 0.95f;
                a.TargetZ = -2.2f + ((i % (count - half)) % 5) * 1.1f;
                a.AttackReach = 1.25f;
                units.Add(a);
            }
            for (int i = 0; i < count - half; i++)
            {
                float x = 4.6f - (i / 5) * 0.95f;
                float z = -2.2f + (i % 5) * 1.1f;
                var e = Agent(half + i + 1, x, z, .38f);
                e.IsTroop = false;
                e.TargetId = 1 + (i % half);
                e.TargetX = -4.6f + ((i % half) / 5) * 0.95f;
                e.TargetZ = -2.2f + ((i % half) % 5) * 1.1f;
                e.AttackReach = 1.25f;
                units.Add(e);
            }

            int prevCap = Application.targetFrameRate;
            int prevVsync = QualitySettings.vSyncCount;
            bool prevProfiler = ProfilerDriver.enabled;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            ProfilerDriver.enabled = true;

            using var nativeRec = ProfilerRecorder.StartNew(new ProfilerCategory("AI"), "NavMeshManager", 1);
            using var avoidRec = ProfilerRecorder.StartNew(new ProfilerCategory("AI"), "Crowd.Avoidance", 1);
            using var carveRec = ProfilerRecorder.StartNew(new ProfilerCategory("AI"), "Carving.ApplyResults", 1);
            using var gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            var bridgeStepMs = new List<double>(512);
            var frameTotalMs = new List<double>(512);
            var nativeFrameMs = new List<double>(512);
            var output = new List<BattleNavigation.AgentOutput>(count);
            long steadyMonoStart = 0;
            int stepCount = 0;

            try
            {
                using var nav = Create();
                float elapsed = 0, accumulator = 0;
                int last = Time.frameCount;
                while (elapsed < duration)
                {
                    await NextFrame();
                    if (last == Time.frameCount) continue;
                    last = Time.frameCount;
                    float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
                    elapsed += dt; accumulator += dt;
                    nav.PrepareFrame(true, false, 1);
                    double frameBridge = 0;
                    while (accumulator >= .05f)
                    {
                        accumulator -= .05f;
                        // 实时更新彼此目标坐标，模拟真实混战目标移动
                        for (int i = 0; i < units.Count; i++)
                        {
                            var u = units[i];
                            int tid = u.TargetId;
                            for (int j = 0; j < units.Count; j++)
                            {
                                if (units[j].Id == tid)
                                {
                                    u.TargetX = units[j].X; u.TargetZ = units[j].Z;
                                    u.HoldPosition = Distance(u, units[j]) <= u.AttackReach - .05f;
                                    break;
                                }
                            }
                            units[i] = u;
                        }
                        if (stepCount == 5) steadyMonoStart = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                        long t0 = Stopwatch.GetTimestamp();
                        nav.Step(units, .05f, output);
                        double stepMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                        bridgeStepMs.Add(stepMs);
                        frameBridge += stepMs;
                        stepCount++;
                        for (int i = 0; i < units.Count; i++)
                        {
                            var a = units[i]; var o = output[i];
                            a.X = o.NewX; a.Z = o.NewZ; units[i] = a;
                        }
                    }
                    double nMs = nativeRec.Valid ? nativeRec.LastValue / 1_000_000.0 : 0.0;
                    nativeFrameMs.Add(nMs);
                    frameTotalMs.Add(frameBridge + nMs);
                }
                long steadyMonoDelta = stepCount > 5 ? Math.Max(0, UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - steadyMonoStart) : 0;
                return new
                {
                    agents = count,
                    steps = bridgeStepMs.Count,
                    frames = frameTotalMs.Count,
                    buildMs = Math.Round(nav.BuildMs, 3),
                    pathRequests = nav.PathRequests,
                    failedPaths = nav.FailedPaths,
                    nativeRecorderValid = nativeRec.Valid,
                    bridgeStepP50Ms = Math.Round(Pct(bridgeStepMs, .50), 4),
                    bridgeStepP95Ms = Math.Round(Pct(bridgeStepMs, .95), 4),
                    bridgeStepP99Ms = Math.Round(Pct(bridgeStepMs, .99), 4),
                    bridgeStepMaxMs = Math.Round(bridgeStepMs.Count > 0 ? bridgeStepMs.Max() : 0, 4),
                    nativeFrameP99Ms = Math.Round(Pct(nativeFrameMs, .99), 4),
                    nativeFrameMaxMs = Math.Round(nativeFrameMs.Count > 0 ? nativeFrameMs.Max() : 0, 4),
                    totalFrameP50Ms = Math.Round(Pct(frameTotalMs, .50), 4),
                    totalFrameP95Ms = Math.Round(Pct(frameTotalMs, .95), 4),
                    totalFrameP99Ms = Math.Round(Pct(frameTotalMs, .99), 4),
                    totalFrameMaxMs = Math.Round(frameTotalMs.Count > 0 ? frameTotalMs.Max() : 0, 4),
                    lastFrameGcBytes = gcRec.Valid ? gcRec.LastValue : -1,
                    steadyMonoUsedDeltaBytes = steadyMonoDelta
                };
            }
            finally
            {
                Application.targetFrameRate = prevCap;
                QualitySettings.vSyncCount = prevVsync;
                ProfilerDriver.enabled = prevProfiler;
            }
        }

        private static double Pct(List<double> list, double f)
        {
            if (list.Count == 0) return 0;
            var arr = list.OrderBy(v => v).ToArray();
            return arr[(int)((arr.Length - 1) * f)];
        }

        private static BattleNavMeshNavigation Create(IList<BattleNavObstacleData> obstacles = null)
            => new BattleNavMeshNavigation(obstacles, new Vector3(1000 + testIndex++ * 50, 0, 0));

        private static async Task Simulate(BattleNavMeshNavigation nav, List<BattleNavigation.AgentInput> units, float duration,
            Action inspect = null, Func<bool> stopWhen = null)
        {
            var output = new List<BattleNavigation.AgentOutput>(64);
            float elapsed = 0, accumulator = 0; int last = Time.frameCount;
            while (elapsed < duration)
            {
                await NextFrame();
                if (last == Time.frameCount) continue; last = Time.frameCount;
                float dt = Mathf.Min(Time.unscaledDeltaTime, .1f); elapsed += dt; accumulator += dt;
                nav.PrepareFrame(true, false, 1);
                while (accumulator >= .05f)
                {
                    accumulator -= .05f; nav.Step(units, .05f, output);
                    for (int i = 0; i < units.Count; i++)
                    {
                        var a = units[i]; var o = output[i];
                        Require(Mathf.Abs(o.NewVx * .05f - (o.NewX - a.X)) < .0001f, "速度等于实际位移/步长");
                        a.X = o.NewX; a.Z = o.NewZ; units[i] = a;
                    }
                    inspect?.Invoke();
                    if (stopWhen != null && stopWhen()) return;
                }
            }
        }

        private static async Task NextFrame()
        {
            int frame = Time.frameCount;
            while (Application.isPlaying && Time.frameCount == frame) await Task.Delay(2);
            if (!Application.isPlaying) throw new OperationCanceledException("Play停止");
        }

        private static float Distance(BattleNavigation.AgentInput a, BattleNavigation.AgentInput b)
            => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

        private static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(current + ": " + message); }
        private static void Result(object result) { results.Add(result); Write("running", null); }

        private static void Write(string state, string error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            string json = JsonConvert.SerializeObject(new
            {
                state, scenario = current, results, error, platform = "WindowsEditor",
                note = "隔离几何/状态/压力验证，不是正常玩法或手机验证"
            }, Formatting.Indented);
            string tmp = Output + ".tmp";
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.WriteAllText(tmp, json);
                    File.Copy(tmp, Output, true);
                    try { File.Delete(tmp); } catch { }
                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(25);
                }
            }
            File.WriteAllText(Output.Replace(".json", "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".json"), json);
        }
    }
}
