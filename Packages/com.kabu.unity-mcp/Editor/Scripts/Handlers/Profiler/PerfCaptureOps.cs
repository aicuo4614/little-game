using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// unity_perf_capture_frame 的纯组装逻辑（可单测）：Game 聚焦、basics/profile/FD/overdraw 块。
    /// </summary>
    internal static class PerfCaptureOps
    {
        public const string Schema = "kabu.perf.frame-capture/v1";

        public static readonly string[] DefaultSections =
        {
            "basics", "profile", "frameDebugger"
        };

        public static readonly string[] RenderCounterNames =
        {
            "Batches Count",
            "Draw Calls Count",
            "SetPass Calls Count",
            "Triangles Count",
            "Vertices Count",
            "Dynamic Batches Count",
            "Static Batches Count",
            "Instanced Batches Count"
        };

        public static bool FocusGameView(out string detail)
        {
            detail = null;

            // 失焦时仍推进 Player/Game（对应 Run In Background）；不抢操作系统前台焦点
            try
            {
                Application.runInBackground = true;
            }
            catch
            {
                // ignore
            }

            // 不调用 BringToForeground / window.Focus / Game 菜单（会抢 OS/编辑器焦点）
            try
            {
                var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType != null)
                {
                    var window = EditorWindow.GetWindow(gameViewType, false, null, focus: false);
                    if (window != null)
                    {
                        detail = "已确保 Game 视口存在（不抢焦点）。";
                        RepaintAndQueue();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                detail = "GetWindow(GameView) 失败：" + ex.Message;
            }

            RepaintAndQueue();

            if (detail == null)
                detail = "已请求 Game 视口相关刷新（不抢焦点）。";
            return true;
        }

        private static void RepaintAndQueue()
        {
            try { InternalEditorUtility.RepaintAllViews(); } catch { /* ignore */ }
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { /* ignore */ }
        }

        public static object BuildContext(
            IList<string> sectionsRequested,
            bool gameViewFocused,
            bool profilerWasEnabled,
            bool frameDebuggerUsed)
        {
            var scene = SceneManager.GetActiveScene();
            return new
            {
                playMode = EditorApplication.isPlaying
                    ? (EditorApplication.isPaused ? "Paused" : "Playing")
                    : "Stopped",
                scenePath = scene.path,
                sceneName = scene.name,
                gameViewFocused,
                profilerWasEnabled,
                frameDebuggerUsed,
                sectionsRequested = sectionsRequested?.ToArray() ?? Array.Empty<string>()
            };
        }

        public static object BuildBasicsSection(
            IReadOnlyDictionary<string, long> counters,
            bool strictBasics,
            out bool basicsOk)
        {
            long Get(string name) =>
                counters != null && counters.TryGetValue(name, out var v) ? v : 0;

            var batches = Get("Batches Count");
            var drawCalls = Get("Draw Calls Count");
            var setPass = Get("SetPass Calls Count");
            var triangles = Get("Triangles Count");
            var vertices = Get("Vertices Count");
            var allZero = batches == 0 && drawCalls == 0 && setPass == 0;

            if (strictBasics && allZero)
            {
                basicsOk = false;
                return new
                {
                    ok = false,
                    status = "failed",
                    error = "Render 关键计数全为 0。通常表示 Game 视口未渲染；请确认 Play Mode 下 Game 有画面后重试。",
                    data = new
                    {
                        batches,
                        drawCalls,
                        setPassCalls = setPass,
                        triangles,
                        vertices,
                        dynamicBatches = Get("Dynamic Batches Count"),
                        staticBatches = Get("Static Batches Count"),
                        instancedBatches = Get("Instanced Batches Count"),
                        source = "ProfilerRecorder/Render"
                    }
                };
            }

            basicsOk = true;
            return new
            {
                ok = true,
                status = "ok",
                error = (string)null,
                data = new
                {
                    batches,
                    drawCalls,
                    setPassCalls = setPass,
                    triangles,
                    vertices,
                    dynamicBatches = Get("Dynamic Batches Count"),
                    staticBatches = Get("Static Batches Count"),
                    instancedBatches = Get("Instanced Batches Count"),
                    source = "ProfilerRecorder/Render"
                }
            };
        }

        public static object BuildSkippedSection(string reason) => new
        {
            ok = false,
            status = "skipped",
            error = reason,
            data = (object)null
        };

        public static object BuildProfileSection(
            object frameTimingData,
            string frameTimingError,
            object hierarchyItems,
            string hierarchyError,
            int? hierarchyFrameIndex,
            out bool profileOk)
        {
            var first = UnityEditorInternal.ProfilerDriver.firstFrameIndex;
            var last = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
            var hasFrames = first >= 0 && last >= first;

            object timingBlock;
            if (frameTimingData != null)
            {
                timingBlock = frameTimingData;
            }
            else
            {
                timingBlock = new
                {
                    available = false,
                    error = frameTimingError ?? "无帧时数据"
                };
            }

            string hierarchyStatus;
            object hierarchyTop;
            string hierarchyNote;
            if (hierarchyItems != null)
            {
                hierarchyStatus = "ok";
                hierarchyTop = hierarchyItems;
                hierarchyNote = "sortBy=selfMs；强制 lastFrame；已过滤 EditorLoop/WaitForJobGroupID；优先 PlayerLoop 子树；drill-down 用 unity_profiler_get_hierarchy_children";
            }
            else
            {
                hierarchyStatus = "unavailable";
                hierarchyTop = Array.Empty<object>();
                hierarchyNote = hierarchyError
                    ?? "Profiler 尚无 Hierarchy 帧；可用 Counters / Frame Debugger，或稍后再采。";
            }

            // profile 块：timing 或 hierarchy 任一可用即 ok；都挂也不拖垮整次 capture
            profileOk = frameTimingData != null || hierarchyItems != null;
            return new
            {
                ok = profileOk,
                status = profileOk ? "ok" : "unavailable",
                error = profileOk ? null : (hierarchyError ?? frameTimingError),
                data = new
                {
                    frameTiming = timingBlock,
                    hierarchyTop,
                    hierarchyStatus,
                    hierarchyNote,
                    hierarchyFrameIndex,
                    profiler = new
                    {
                        enabled = UnityEngine.Profiling.Profiler.enabled,
                        hasFrames,
                        firstFrameIndex = first,
                        lastFrameIndex = last
                    }
                }
            };
        }

        public static object BuildFrameDebuggerSection(int fdEventLimit, out bool fdOk)
        {
            // 0 = 只要 eventCount + summaryByType，不返回 topEvents（省 token；列表非耗时排序）
            // >0 = 按绘制顺序取前 N 条（最大 2000）；负值按 0 处理
            var includeTop = fdEventLimit > 0;
            var limit = includeTop ? Math.Clamp(fdEventLimit, 1, 2000) : 0;

            if (!FrameDebuggerApi.Available)
            {
                fdOk = false;
                return new
                {
                    ok = false,
                    status = "unavailable",
                    error = FrameDebuggerApi.UnavailableReason,
                    data = (object)null
                };
            }

            var total = FrameDebuggerApi.GetCount();
            if (total <= 0)
            {
                fdOk = false;
                return new
                {
                    ok = false,
                    status = "failed",
                    error = "Frame Debugger 事件数为 0。确认 Game 视口有画面后再 capture。",
                    data = BuildFdData(
                        eventCount: 0,
                        summary: new Dictionary<string, int>(),
                        topEvents: null,
                        includeTop: includeTop,
                        limit: limit)
                };
            }

            var frameEvents = FrameDebuggerApi.GetFrameEvents();
            var summary = new Dictionary<string, int>(StringComparer.Ordinal);
            var topEvents = includeTop ? new List<object>(limit) : null;

            for (var i = 0; i < total; i++)
            {
                object desc = null;
                if (frameEvents != null && i < frameEvents.Length)
                    desc = frameEvents.GetValue(i);

                var typeName = FrameDebuggerApi.GetEventTypeName(desc) ?? "Unknown";
                if (!summary.ContainsKey(typeName))
                    summary[typeName] = 0;
                summary[typeName]++;

                if (includeTop && i < limit)
                {
                    var infoName = FrameDebuggerApi.GetEventInfoName(i);
                    var bound = FrameDebuggerApi.GetEventBoundObject(desc)
                                ?? FrameDebuggerApi.GetEventObject(i);
                    FrameDebuggerOps.ResolveObjectNames(bound, out var goName, out var path);
                    topEvents.Add(new
                    {
                        index = i,
                        type = typeName,
                        name = Shorten(infoName, 120),
                        gameObject = goName,
                        path
                    });
                }
            }

            fdOk = true;
            return new
            {
                ok = true,
                status = "ok",
                error = (string)null,
                data = BuildFdData(total, summary, topEvents, includeTop, limit)
            };
        }

        /// <summary>
        /// 用 Dictionary 组装 FD data，以便 fdEventLimit=0 时省略 topEvents 键。
        /// </summary>
        private static Dictionary<string, object> BuildFdData(
            int eventCount,
            Dictionary<string, int> summary,
            List<object> topEvents,
            bool includeTop,
            int limit)
        {
            var data = new Dictionary<string, object>
            {
                ["eventCount"] = eventCount,
                ["pausedPlayMode"] = EditorApplication.isPaused,
                ["summaryByType"] = summary ?? new Dictionary<string, int>(),
                ["returned"] = includeTop ? (topEvents?.Count ?? 0) : 0,
                ["nextOffset"] = includeTop && limit < eventCount ? (object)limit : null,
            };

            if (includeTop)
            {
                data["topEvents"] = topEvents ?? (object)Array.Empty<object>();
                data["note"] =
                    "topEvents = 按绘制顺序前 N 条（非耗时排序）；详情用 unity_frame_debugger_get_event";
            }
            else
            {
                data["note"] =
                    "fdEventLimit=0：未返回 topEvents（仅 eventCount/summaryByType）；详情用 unity_frame_debugger_list / get_event";
            }

            return data;
        }

        /// <summary>历史名保留；现委托 OverdrawOps（独立 tool 同源）。</summary>
        public static object BuildOverdrawPlaceholder() => OverdrawOps.BuildSectionForCapture();

        public static object BuildRoot(
            bool ok,
            IList<string> warnings,
            object context,
            object basics,
            object profile,
            object frameDebugger,
            object overdraw)
        {
            return new
            {
                schema = Schema,
                capturedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                ok,
                warnings = warnings?.ToArray() ?? Array.Empty<string>(),
                context,
                basics,
                profile,
                frameDebugger,
                overdraw
            };
        }

        public static HashSet<string> ParseSections(IEnumerable<string> sections)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sections == null)
            {
                foreach (var s in DefaultSections)
                    set.Add(s);
                return set;
            }

            foreach (var s in sections)
            {
                if (string.IsNullOrWhiteSpace(s))
                    continue;
                set.Add(s.Trim());
            }

            if (set.Count == 0)
            {
                foreach (var s in DefaultSections)
                    set.Add(s);
            }

            return set;
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s;
            return s.Substring(0, max) + "…";
        }
    }
}
