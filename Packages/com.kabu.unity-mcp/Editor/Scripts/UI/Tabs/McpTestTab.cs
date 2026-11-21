using System;
using KabuUnityMcp.Editor.Handlers.Profiler;
using KabuUnityMcp.Editor.Services;
using UnityEditor;
using UnityEngine;
using UnityEditorInternal;
using Newtonsoft.Json;

namespace KabuUnityMcp.Editor.UI.Tabs
{
    /// <summary>
    /// 单元测试 + 接口手动试跑（不经 MCP 客户端，直接调 Editor 编排）。
    /// </summary>
    internal sealed class McpTestTab
    {
        private string _memoryPath = "";
        private string _memoryCompareA = "";
        private string _memoryCompareB = "";
        private int _memoryTargetId = -1;
        private int _memoryFlags = 7;
        private int _memoryTimeout = 180;
        private Vector2 _pageScroll;
        private Vector2 _unitScroll;
        private Vector2 _apiScroll;
        private string _apiStatus = "空闲";
        private string _apiJson = "";
        private string _apiLastLabel = "";
        private bool _apiLastOk;
        private bool _apiCopied;
        private GUIStyle _jsonStyle;

        public void OnEnable()
        {
            PerfCaptureHandlers.Changed += OnPerfChanged;
            OverdrawAbHandlers.Changed += OnPerfChanged;
        }

        public void OnDisable()
        {
            PerfCaptureHandlers.Changed -= OnPerfChanged;
            OverdrawAbHandlers.Changed -= OnPerfChanged;
        }

        private void OnPerfChanged()
        {
            // Window 会订阅并 Repaint
        }

        public void Draw()
        {
            _pageScroll = EditorGUILayout.BeginScrollView(_pageScroll);
            DrawMemorySection();
            EditorGUILayout.Space(10);
            DrawApiManualSection();
            EditorGUILayout.Space(14);
            DrawUnitTestSection();
            EditorGUILayout.EndScrollView();
        }

        private void DrawMemorySection()
        {
            EditorGUILayout.LabelField("Memory Profiler", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("真机请先运行 Development Build，并在 Unity Profiler 中连接设备。选择目标后再采集；结果包含采集目标信息。快照文件大小不能作为内存泄漏结论。", MessageType.Info);
            var ids = ProfilerDriver.GetAvailableProfilers();
            var names = Array.ConvertAll(ids, id => $"{id}: {ProfilerDriver.GetConnectionIdentifier(id)}");
            var index = Array.IndexOf(ids, _memoryTargetId);
            using (new EditorGUI.DisabledScope(ProfilerMemorySnapshotOps.IsBusy))
            {
                var next = EditorGUILayout.Popup("采集目标", index, names);
                if (next >= 0 && next < ids.Length) _memoryTargetId = ids[next];
                if (GUILayout.Button("查询目标 / 采集状态")) ApplyMemoryResult(ProfilerMemoryTargetOps.List(), "memory_targets");
                using (new EditorGUI.DisabledScope(_memoryTargetId == -1))
                    if (GUILayout.Button("连接所选目标")) ApplyMemoryResult(ProfilerMemoryTargetOps.Select(_memoryTargetId), "memory_select_target");
                EditorGUILayout.LabelField("当前连接", ProfilerDriver.GetConnectionIdentifier(ProfilerDriver.connectedProfiler));
                _memoryPath = EditorGUILayout.TextField("新快照路径（可留空）", _memoryPath);
                _memoryFlags = EditorGUILayout.IntField("Capture flags (默认 7)", _memoryFlags);
                _memoryTimeout = EditorGUILayout.IntField("超时秒数 (10-600)", _memoryTimeout);
                using (new EditorGUI.DisabledScope(_memoryTargetId == -1))
                {
                    if (GUILayout.Button("采集所选目标 Memory Snapshot", GUILayout.Height(30)))
                    {
                        _apiStatus = "Memory capture running...";
                        _apiJson = "";
                        if (!ProfilerMemorySnapshotOps.TryBeginTake(_memoryPath,
                            result => ApplyMemoryResult(result, "memory_take_snapshot"), out var error,
                            _memoryTargetId, _memoryFlags, _memoryTimeout))
                            ApplyApiResult(false, error, "memory_take_snapshot");
                    }
                }
            }
            if (ProfilerMemorySnapshotOps.IsBusy)
                EditorGUILayout.HelpBox("原生快照采集中。请求超时不会取消原生采集，请等待回调完成。", MessageType.Info);
            if (GUILayout.Button("列出快照")) ApplyMemoryResult(ProfilerMemorySnapshotOps.List(null, 0, 50), "memory_list_snapshots");
            _memoryCompareA = EditorGUILayout.TextField("对比快照 A", _memoryCompareA);
            _memoryCompareB = EditorGUILayout.TextField("对比快照 B", _memoryCompareB);
            if (GUILayout.Button("对比文件元数据（不分析对象内存）"))
                ApplyMemoryResult(ProfilerMemorySnapshotOps.Compare(_memoryCompareA, _memoryCompareB), "memory_compare_snapshots");
        }

        private void ApplyMemoryResult(KabuUnityMcp.Editor.Handlers.HandlerResult result, string label)
        {
            ApplyApiResult(result.Ok, result.Ok ? JsonConvert.SerializeObject(result.Payload, Formatting.Indented) : result.Error, label);
        }

        private void DrawApiManualSection()
        {
            EditorGUILayout.LabelField("接口手动试跑", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "不经过 Cursor/MCP HTTP，直接在 Editor 内调用与 MCP tool 相同的编排。\n" +
                "• capture_frame：未在 Play 时会自动进入 Play；建议点一下 Game 视口确认有画面。\n" +
                "• get_overdraw：Play/Game Additive 整屏层数 + 候选开关 A/B（avgLayersDrop=贡献层数）；自动进 Play，较慢。\n" +
                "完成后 JSON 会自动复制到系统剪贴板，也可再点「复制 JSON」。",
                MessageType.Info);

            var busy = PerfCaptureHandlers.IsBusy || OverdrawAbHandlers.IsBusy;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !busy;
                if (GUILayout.Button(
                        PerfCaptureHandlers.IsBusy ? "采帧中…" : "▶  调用 unity_perf_capture_frame",
                        GUILayout.Height(36)))
                {
                    _apiStatus = "运行中…";
                    _apiJson = "";
                    _apiCopied = false;
                    _apiLastOk = false;
                    _apiLastLabel = "perf_capture_frame";
                    PerfCaptureHandlers.CaptureLocal(OnCaptureLocalDone);
                }

                GUI.enabled = !busy && !string.IsNullOrEmpty(_apiJson);
                if (GUILayout.Button("复制 JSON", GUILayout.Height(36), GUILayout.Width(96)))
                {
                    var label = string.IsNullOrEmpty(_apiLastLabel) ? "api_json" : _apiLastLabel;
                    McpEditorGui.CopyToClipboard(label, _apiJson);
                    _apiCopied = true;
                }
                GUI.enabled = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !busy;
                if (GUILayout.Button(
                        OverdrawAbHandlers.IsBusy
                            ? "过绘 A/B 中…"
                            : "▶  调用 unity_perf_get_overdraw（Play A/B 层数）",
                        GUILayout.Height(36)))
                {
                    RunOverdrawLocal();
                }
                GUI.enabled = true;
            }

            EditorGUILayout.Space(4);
            var play = EditorApplication.isPlaying
                ? (EditorApplication.isPaused ? "Paused" : "Playing")
                : "Stopped";
            EditorGUILayout.LabelField("Play Mode", play);
            EditorGUILayout.LabelField("状态", _apiStatus);
            if (_apiCopied)
                EditorGUILayout.LabelField("剪贴板", "已复制");

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("最近一次 JSON", EditorStyles.boldLabel);
            _apiScroll = EditorGUILayout.BeginScrollView(_apiScroll, GUILayout.MinHeight(160), GUILayout.MaxHeight(280));
            if (_jsonStyle == null)
            {
                _jsonStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true,
                    richText = false,
                    fontSize = 11
                };
            }

            EditorGUILayout.TextArea(
                string.IsNullOrEmpty(_apiJson) ? "（还没有结果）" : _apiJson,
                _jsonStyle,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        /// <summary>与 MCP tool 同源：异步 Play A/B，结果复制剪贴板。</summary>
        private void RunOverdrawLocal()
        {
            _apiLastLabel = "perf_get_overdraw";
            _apiStatus = "运行中（Play A/B）…";
            _apiJson = "";
            _apiCopied = false;
            _apiLastOk = false;
            OverdrawAbHandlers.CaptureLocal(OnOverdrawLocalDone);
        }

        private void OnOverdrawLocalDone(bool ok, string jsonOrError)
        {
            ApplyApiResult(ok, jsonOrError, "perf_get_overdraw");
        }

        private void OnCaptureLocalDone(bool ok, string jsonOrError)
        {
            ApplyApiResult(ok, jsonOrError, "perf_capture_frame");
        }

        private void ApplyApiResult(bool ok, string jsonOrError, string label)
        {
            _apiLastLabel = label;
            _apiLastOk = ok;
            if (ok || (jsonOrError != null && jsonOrError.TrimStart().StartsWith("{", StringComparison.Ordinal)))
            {
                _apiJson = jsonOrError ?? "";
                _apiStatus = ok ? $"完成（{label}，ok=true）" : $"完成（{label}，ok=false，见 JSON / warnings）";
                if (!string.IsNullOrEmpty(_apiJson))
                {
                    EditorGUIUtility.systemCopyBuffer = _apiJson;
                    _apiCopied = true;
                    Debug.Log($"[kabu-unity-mcp] {label} JSON 已复制到剪贴板。");
                }
            }
            else
            {
                _apiJson = "";
                _apiStatus = $"失败（{label}）：" + jsonOrError;
                _apiCopied = false;
                Debug.LogError($"[kabu-unity-mcp] {label} 失败：" + jsonOrError);
            }
        }

        private void DrawUnitTestSection()
        {
            var runner = McpEditModeTestRunner.Instance;

            EditorGUILayout.LabelField("单元测试（EditMode）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "使用 Unity 自带 Test Framework（com.unity.test-framework），无需额外插件。\n" +
                "点「运行全部」会执行 Tests/Editor 下程序集 kabu-unity-mcp.Editor.Tests。\n" +
                "本地 file 包工程须在 Packages/manifest.json 写上：\n" +
                "  \"testables\": [ \"com.kabu.unity-mcp\" ]\n" +
                "也可打开菜单 Window → General → Test Runner 查看同样结果。",
                MessageType.Info);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !runner.IsRunning;
                if (GUILayout.Button(runner.IsRunning ? "运行中…" : "运行全部测试", GUILayout.Height(32)))
                    runner.RunAll();

                GUI.enabled = !runner.IsRunning && runner.HasResult;
                if (GUILayout.Button("清空日志", GUILayout.Height(32), GUILayout.Width(88)))
                    runner.Clear();
                GUI.enabled = true;
            }

            EditorGUILayout.Space(8);
            DrawSummary(runner);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("输出", EditorStyles.boldLabel);
            _unitScroll = EditorGUILayout.BeginScrollView(_unitScroll, GUILayout.MinHeight(120));
            var style = EditorStyles.wordWrappedLabel;
            EditorGUILayout.TextArea(
                string.IsNullOrEmpty(runner.LogText) ? "（还没有输出）" : runner.LogText,
                style,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private static void DrawSummary(McpEditModeTestRunner runner)
        {
            var box = EditorStyles.helpBox;
            EditorGUILayout.BeginVertical(box);

            EditorGUILayout.LabelField("状态", runner.IsRunning ? "运行中" : (runner.HasResult ? "已完成" : "空闲"));
            EditorGUILayout.LabelField("摘要", runner.Summary);

            if (runner.HasResult)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawStat("通过", runner.Passed, new Color(0.35f, 0.75f, 0.45f));
                    DrawStat("失败", runner.Failed, new Color(0.9f, 0.35f, 0.35f));
                    DrawStat("跳过", runner.Skipped, new Color(0.7f, 0.7f, 0.4f));
                    DrawStat("合计", runner.Total, EditorStyles.label.normal.textColor);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawStat(string label, int value, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            EditorGUILayout.LabelField($"{label} {value}", EditorStyles.boldLabel, GUILayout.MinWidth(64));
            GUI.color = prev;
        }
    }
}
