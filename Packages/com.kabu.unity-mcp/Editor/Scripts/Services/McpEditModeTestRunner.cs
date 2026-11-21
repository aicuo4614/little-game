using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace KabuUnityMcp.Editor.Services
{
    /// <summary>
    /// Runs package EditMode tests via Unity Test Runner API (no third-party plugin).
    /// 工程 Packages/manifest.json 须含 testables: ["com.kabu.unity-mcp"]，否则测试程序集不会编译。
    /// </summary>
    internal sealed class McpEditModeTestRunner : ScriptableObject, ICallbacks
    {
        public const string TestsAssemblyName = "kabu-unity-mcp.Editor.Tests";
        public const string PackageName = "com.kabu.unity-mcp";

        private static McpEditModeTestRunner _instance;
        private TestRunnerApi _api;
        private readonly List<string> _lines = new List<string>();
        private readonly StringBuilder _log = new StringBuilder();
        private bool _pendingExecute;

        public bool IsRunning { get; private set; }
        public bool HasResult { get; private set; }
        public int Passed { get; private set; }
        public int Failed { get; private set; }
        public int Skipped { get; private set; }
        public int Total { get; private set; }
        public string Summary { get; private set; } = "尚未运行。";
        public string LogText => _log.ToString();

        public event Action Changed;

        public static McpEditModeTestRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateInstance<McpEditModeTestRunner>();
                    _instance.hideFlags = HideFlags.HideAndDontSave;
                }
                return _instance;
            }
        }

        public void RunAll()
        {
            if (IsRunning)
                return;

            EnsureApi();
            _lines.Clear();
            _log.Length = 0;
            Passed = Failed = Skipped = Total = 0;
            HasResult = false;
            IsRunning = true;
            _pendingExecute = true;
            Summary = "正在检索 EditMode 测试列表…";
            Append("开始运行程序集：" + TestsAssemblyName);
            RaiseChanged();

            try
            {
                _api.RetrieveTestList(TestMode.EditMode, OnTestListRetrieved);
            }
            catch (Exception ex)
            {
                FailStart("检索测试列表失败：" + ex.Message);
            }
        }

        public void Clear()
        {
            if (IsRunning)
                return;
            _lines.Clear();
            _log.Length = 0;
            Passed = Failed = Skipped = Total = 0;
            HasResult = false;
            Summary = "尚未运行。";
            RaiseChanged();
        }

        private void OnTestListRetrieved(ITestAdaptor root)
        {
            if (!_pendingExecute)
                return;
            _pendingExecute = false;

            try
            {
                var loaded = IsAssemblyLoaded(TestsAssemblyName);
                var assemblyNode = FindAssemblyNode(root, TestsAssemblyName);
                var assemblySuites = ListAssemblySuiteNames(root).ToList();

                Append($"AppDomain 已加载程序集：{(loaded ? "是" : "否")}");
                Append($"Test Tree 中的程序集节点数：{assemblySuites.Count}");
                if (assemblySuites.Count > 0)
                {
                    var preview = string.Join(", ", assemblySuites.Take(12));
                    if (assemblySuites.Count > 12)
                        preview += ", …";
                    Append("Test Tree 程序集：" + preview);
                }

                if (assemblyNode == null && !loaded)
                {
                    FailStart(
                        $"未找到测试程序集 {TestsAssemblyName}（DLL 未加载，Test Tree 也无节点）。\n" +
                        $"请在工程 Packages/manifest.json 加入：\n" +
                        $"  \"testables\": [ \"{PackageName}\" ]\n" +
                        "保存后等 Unity 重新编译，并打开 Window → General → Test Runner 确认该程序集出现。");
                    return;
                }

                if (assemblyNode == null && loaded)
                {
                    // 树缓存可能滞后；程序集已加载则直接按 Filter 跑。
                    Append("警告：Test Tree 暂无匹配节点，但程序集已加载，继续按 assemblyNames Filter 执行。");
                }
                else
                {
                    var leafCount = CountLeafTests(assemblyNode);
                    Append($"已匹配程序集节点 '{DescribeNode(assemblyNode)}'，用例数 {leafCount}");
                    if (leafCount == 0)
                    {
                        FailStart($"程序集 {TestsAssemblyName} 下没有可运行用例。");
                        return;
                    }
                }

                Summary = "正在运行 EditMode 测试…";
                RaiseChanged();

                var filter = new Filter
                {
                    testMode = TestMode.EditMode,
                    assemblyNames = new[] { TestsAssemblyName }
                };
                _api.Execute(new ExecutionSettings(filter));
            }
            catch (Exception ex)
            {
                FailStart("启动失败：" + ex.Message);
            }
        }

        private void FailStart(string message)
        {
            IsRunning = false;
            HasResult = true;
            Summary = message;
            Append(Summary);
            RaiseChanged();
            EditorApplication.delayCall += () =>
                Debug.LogError($"[kabu-unity-mcp] 测试失败：{Summary}\n{LogText}");
        }

        private void EnsureApi()
        {
            if (_api != null)
                return;
            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _api.RegisterCallbacks(this);
        }

        void ICallbacks.RunStarted(ITestAdaptor testsToRun)
        {
            Append($"RunStarted — 计划用例数约 {CountLeafTests(testsToRun)}");
            RaiseChanged();
        }

        void ICallbacks.RunFinished(ITestResultAdaptor result)
        {
            IsRunning = false;
            HasResult = true;
            Recount(result);

            if (Total == 0)
            {
                Summary =
                    "没有跑到用例。确认程序集 " + TestsAssemblyName + " 已编译，且工程 " +
                    "Packages/manifest.json 的 testables 包含 \"" + PackageName + "\"。";
            }
            else if (Failed > 0)
            {
                Summary = $"失败 {Failed} / 通过 {Passed}（共 {Total}）";
            }
            else
            {
                Summary = $"全部通过 {Passed}（共 {Total}）";
            }

            Append("—— 结束 ——");
            Append(Summary);
            RaiseChanged();
            EditorApplication.delayCall += () =>
            {
                if (Total == 0 || Failed > 0)
                    Debug.LogError($"[kabu-unity-mcp] 测试失败：{Summary}\n{LogText}");
                else
                    Debug.Log($"[kabu-unity-mcp] 测试通过：{Summary}");
            };
        }

        void ICallbacks.TestStarted(ITestAdaptor test)
        {
            if (!test.IsSuite)
                Append($"→ {test.FullName}");
            RaiseChanged();
        }

        void ICallbacks.TestFinished(ITestResultAdaptor result)
        {
            if (result.Test.IsSuite)
                return;

            var status = result.TestStatus.ToString();
            var line = $"  [{status}] {result.Test.FullName}";
            if (result.TestStatus == TestStatus.Failed && !string.IsNullOrEmpty(result.Message))
                line += $"\n    {result.Message.Trim()}";
            Append(line);
            RaiseChanged();
        }

        private void Recount(ITestResultAdaptor root)
        {
            Passed = root.PassCount;
            Failed = root.FailCount;
            Skipped = root.SkipCount + root.InconclusiveCount;
            Total = Passed + Failed + Skipped;

            if (Total > 0)
                return;

            Passed = Failed = Skipped = Total = 0;
            Walk(root);
        }

        private void Walk(ITestResultAdaptor node)
        {
            if (!node.Test.IsSuite)
            {
                Total++;
                switch (node.TestStatus)
                {
                    case TestStatus.Passed: Passed++; break;
                    case TestStatus.Failed: Failed++; break;
                    default: Skipped++; break;
                }
                return;
            }

            if (node.Children == null)
                return;
            foreach (var child in node.Children)
                Walk(child);
        }

        private static bool IsAssemblyLoaded(string assemblyName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (string.Equals(name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static ITestAdaptor FindAssemblyNode(ITestAdaptor node, string assemblyName)
        {
            if (node == null)
                return null;

            if (IsMatchingAssemblyNode(node, assemblyName))
                return node;

            if (node.Children == null)
                return null;

            foreach (var child in node.Children)
            {
                var found = FindAssemblyNode(child, assemblyName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static bool IsMatchingAssemblyNode(ITestAdaptor node, string assemblyName)
        {
            if (!node.IsSuite)
                return false;

            // UTF 1.1+：优先用官方标记
            if (node.IsTestAssembly)
                return NameMatchesAssembly(node.Name, assemblyName)
                       || NameMatchesAssembly(node.FullName, assemblyName)
                       || NameMatchesAssembly(node.UniqueName, assemblyName);

            // 回退：名称像程序集节点
            return NameMatchesAssembly(node.Name, assemblyName)
                   || NameMatchesAssembly(node.FullName, assemblyName);
        }

        private static bool NameMatchesAssembly(string candidate, string assemblyName)
        {
            if (string.IsNullOrEmpty(candidate))
                return false;

            // UniqueName 可能是 "Asm.dll/" 或带路径分隔
            var normalized = candidate.Replace('\\', '/');
            var slash = normalized.IndexOf('/');
            if (slash >= 0)
                normalized = normalized.Substring(0, slash);

            if (normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 4);

            return string.Equals(normalized, assemblyName, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> ListAssemblySuiteNames(ITestAdaptor root)
        {
            foreach (var child in EnumerateDepth(root, 0, 4))
            {
                if (child.IsTestAssembly)
                    yield return DescribeNode(child);
            }
        }

        private static IEnumerable<ITestAdaptor> EnumerateDepth(ITestAdaptor node, int depth, int maxDepth)
        {
            if (node == null || depth > maxDepth)
                yield break;

            yield return node;
            if (node.Children == null)
                yield break;
            foreach (var child in node.Children)
            {
                foreach (var n in EnumerateDepth(child, depth + 1, maxDepth))
                    yield return n;
            }
        }

        private static string DescribeNode(ITestAdaptor node)
        {
            if (node == null)
                return "(null)";
            var name = node.Name ?? "";
            var full = node.FullName ?? "";
            if (string.Equals(name, full, StringComparison.Ordinal))
                return $"{name}[asm={node.IsTestAssembly}]";
            return $"{name} / {full}[asm={node.IsTestAssembly}]";
        }

        private static int CountLeafTests(ITestAdaptor node)
        {
            if (node == null)
                return 0;
            if (!node.IsSuite)
                return 1;

            // TestCaseCount 在 UTF 上更可靠
            try
            {
                if (node.TestCaseCount > 0)
                    return node.TestCaseCount;
            }
            catch
            {
                // ignore
            }

            var n = 0;
            if (node.Children == null)
                return 0;
            foreach (var c in node.Children)
                n += CountLeafTests(c);
            return n;
        }

        private void Append(string line)
        {
            _lines.Add(line);
            _log.AppendLine(line);
        }

        private void RaiseChanged()
        {
            EditorApplication.delayCall += () => Changed?.Invoke();
        }
    }
}
