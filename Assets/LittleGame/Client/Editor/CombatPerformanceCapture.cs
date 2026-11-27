using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LittleGame.Combat;
using Newtonsoft.Json;
using Unity.Profiling;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace LittleGame.Client.Editor
{
    /// <summary>编辑器无限帧率逐帧采样。采样期间使用预分配结构体缓存，停止后写盘。</summary>
    [InitializeOnLoad]
    public static class CombatPerformanceCapture
    {
        private struct FrameSample
        {
            public int frame, wave, units, enemies;
            public RunPhase phase;
            public float intervalMs;
            public double mainMs, renderMs, rulesMs, viewMs, hudMs, navigationMs, nativeNavigationMs, avoidanceMs, carvingMs;
            public long gcBytes;
        }

        private static readonly List<FrameSample> samples = new List<FrameSample>(100000);
        private static ProfilerRecorder main, render, rules, view, hud, gc, nativeNavigation, avoidance, carving;
        private static bool running, previousProfiler;
        private static int previousCap, previousVsync, lastFrame;
        private static string lastPath;
        private static long lastNavigationTicks;

        static CombatPerformanceCapture()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Restore;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode && running) Stop();
            };
        }

        public static object Start()
        {
            if (!Application.isPlaying || ClientBootstrap.Current == null)
                throw new InvalidOperationException("请先进入战斗 Play。");
            if (running) throw new InvalidOperationException("性能采样已在进行。");
            previousCap = Application.targetFrameRate;
            previousVsync = QualitySettings.vSyncCount;
            previousProfiler = ProfilerDriver.enabled;
            samples.Clear();
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            ProfilerDriver.enabled = true;
            main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            render = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1);
            rules = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "LittleGame.Rules", 1);
            view = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "LittleGame.View", 1);
            hud = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "LittleGame.Hud", 1);
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            nativeNavigation = ProfilerRecorder.StartNew(new ProfilerCategory("AI"), "NavMeshManager", 1);
            avoidance = ProfilerRecorder.StartNew(new ProfilerCategory("AI"), "Crowd.Avoidance", 1);
            carving = ProfilerRecorder.StartNew(new ProfilerCategory("AI"), "Carving.ApplyResults", 1);
            running = true;
            lastFrame = Time.frameCount;
            lastNavigationTicks = ClientBootstrap.Current.App.Session.NavigationCpuTicks;
            return Status();
        }

        public static object Status() => new
        {
            running, samples = samples.Count, lastPath,
            targetFrameRate = Application.targetFrameRate,
            vsync = QualitySettings.vSyncCount,
            recorders = new { main = main.Valid, render = render.Valid, rules = rules.Valid, view = view.Valid, hud = hud.Valid, gc = gc.Valid, nativeNavigation = nativeNavigation.Valid, avoidance = avoidance.Valid, carving = carving.Valid }
        };

        private static double Milliseconds(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue / 1000000.0 : -1;

        private static void Tick()
        {
            if (!running || !Application.isPlaying || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            var boot = ClientBootstrap.Current;
            if (boot == null || boot.App == null || samples.Count >= samples.Capacity) return;
            var session = boot.App.Session;
            long navigationTicks = session.NavigationCpuTicks;
            double navigationMs = Math.Max(0, navigationTicks - lastNavigationTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            lastNavigationTicks = navigationTicks;
            samples.Add(new FrameSample
            {
                frame = lastFrame, wave = session.Wave, phase = session.Phase,
                units = session.DeployedCount, enemies = session.Enemies.Count(e => e.Alive),
                intervalMs = Time.unscaledDeltaTime * 1000f,
                mainMs = Milliseconds(main), renderMs = Milliseconds(render),
                rulesMs = Milliseconds(rules), viewMs = Milliseconds(view), hudMs = Milliseconds(hud),
                navigationMs = navigationMs, nativeNavigationMs = Milliseconds(nativeNavigation), avoidanceMs = Milliseconds(avoidance), carvingMs = Milliseconds(carving),
                gcBytes = gc.Valid ? gc.LastValue : -1
            });
        }

        public static object Stop()
        {
            if (!running) return Status();
            bool nativeAvailable = nativeNavigation.Valid;
            Restore();
            Directory.CreateDirectory("Reports/Performance/20261127");
            lastPath = Path.GetFullPath("Reports/Performance/20261127/uncapped-" + DateTime.Now.ToString("HHmmss") + ".json");
            var fighting = samples.Where(s => s.phase == RunPhase.Fighting).ToArray();
            File.WriteAllText(lastPath, JsonConvert.SerializeObject(new
            {
                platform = Application.platform.ToString(), width = Screen.width, height = Screen.height,
                targetFrameRate = -1, vsync = 0, navigationBackend = ClientBootstrap.Current?.App?.Navigation?.Backend,
                nativeNavigationAvailable = nativeAvailable,
                note = "Unity Editor、Profiler 开启、非 Deep Profile；逐帧耗时含编辑器和采样开销，不能据此宣称手机帧率。采样完成后恢复原帧率设置。",
                fightingFrames = fighting.Length,
                summary = fighting.GroupBy(s => s.wave).Select(g => new
                {
                    wave = g.Key, frames = g.Count(),
                    meanFrameMs = g.Average(s => s.intervalMs), p95FrameMs = Percentile(g.Select(s => (double)s.intervalMs), .95),
                    meanMainMs = g.Average(s => s.mainMs), p95MainMs = Percentile(g.Select(s => s.mainMs), .95),
                    maxMainMs = g.Max(s => s.mainMs), meanRenderMs = g.Average(s => s.renderMs),
                    meanRulesMs = g.Average(s => s.rulesMs), maxRulesMs = g.Max(s => s.rulesMs),
                    meanNavigationMs = g.Average(s => s.navigationMs), maxNavigationMs = g.Max(s => s.navigationMs),
                    meanNativeNavigationMs = g.Average(s => s.nativeNavigationMs), maxNativeNavigationMs = g.Max(s => s.nativeNavigationMs),
                    p99NavigationTotalMs = Percentile(g.Select(s => s.navigationMs + Math.Max(0, s.nativeNavigationMs)), .99),
                    maxNavigationTotalMs = g.Max(s => s.navigationMs + Math.Max(0, s.nativeNavigationMs)),
                    maxAvoidanceMs = g.Max(s => s.avoidanceMs), maxCarvingMs = g.Max(s => s.carvingMs),
                    meanViewMs = g.Average(s => s.viewMs), maxViewMs = g.Max(s => s.viewMs),
                    meanHudMs = g.Average(s => s.hudMs), maxHudMs = g.Max(s => s.hudMs),
                    meanGcBytes = g.Average(s => s.gcBytes), maxGcBytes = g.Max(s => s.gcBytes)
                }),
                frames = samples
            }, Formatting.Indented));
            return new { path = lastPath, frames = samples.Count, fightingFrames = fighting.Length };
        }

        private static double Percentile(IEnumerable<double> source, double fraction)
        {
            var values = source.OrderBy(v => v).ToArray();
            return values.Length == 0 ? 0 : values[(int)((values.Length - 1) * fraction)];
        }

        private static void Restore()
        {
            if (!running) return;
            running = false;
            main.Dispose(); render.Dispose(); rules.Dispose(); view.Dispose(); hud.Dispose(); gc.Dispose();
            nativeNavigation.Dispose(); avoidance.Dispose(); carving.Dispose();
            Application.targetFrameRate = previousCap;
            QualitySettings.vSyncCount = previousVsync;
            ProfilerDriver.enabled = previousProfiler;
        }
    }
}
