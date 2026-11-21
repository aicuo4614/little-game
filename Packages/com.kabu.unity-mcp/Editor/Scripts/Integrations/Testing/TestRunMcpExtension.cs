using System;
using System.Collections.Generic;
using System.IO;
using KabuUnityMcp.Editor.Extensions;
using KabuUnityMcp.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace KabuUnityMcp.Editor.Integrations.Testing
{
    /// <summary>Asynchronous, explicitly scoped EditMode test runs. No project-specific test names.</summary>
    internal sealed class TestRunMcpExtension : IMcpExtension, ICallbacks
    {
        private const string SessionKey = "KabuUnityMcp.TestRun";
        private TestRunnerApi api;
        private JObject current;
        private readonly List<object> failures = new List<object>();
        private bool ownsRun;

        public string Id => "tests";
        public string Version => "1.0.0";
        public string Description => "Start a scoped EditMode test run and poll its result. Tests may modify the project; no automatic replay after reload.";
        public string UnavailableReason => null;
        public McpExtensionOperation[] Operations => new[]
        {
            new McpExtensionOperation("run", "Queue EditMode tests from one exact assemblyName, optionally one exact testName. Returns runId immediately. Requires allowWrite=true. Cancellation is not supported by the bundled TestRunner API.", false,
                new JObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JArray("assemblyName"),
                    ["properties"] = new JObject { ["assemblyName"] = new JObject { ["type"] = "string" }, ["testName"] = new JObject { ["type"] = "string" } } }),
            new McpExtensionOperation("status", "Read the latest test run, optionally verifying its runId. Reload interrupts observation and reports interrupted rather than success.", true,
                new JObject { ["type"] = "object", ["additionalProperties"] = false,
                    ["properties"] = new JObject { ["runId"] = new JObject { ["type"] = "string" } } })
        };

        [InitializeOnLoadMethod]
        private static void Register()
        {
            var extension = new TestRunMcpExtension();
            var previous = SessionState.GetString(SessionKey, "");
            if (!string.IsNullOrEmpty(previous))
            {
                extension.current = JObject.Parse(previous);
                if ((string)extension.current["state"] == "running" || (string)extension.current["state"] == "queued")
                {
                    extension.current["state"] = "interrupted";
                    extension.current["message"] = "Assembly reload interrupted test observation; execution outcome may be unknown. No automatic replay.";
                    extension.Persist();
                }
            }
            McpExtensionRegistry.Register(extension);
            AssemblyReloadEvents.beforeAssemblyReload += extension.BeforeReload;
        }

        public McpHostResult Execute(string operation, JObject arguments)
        {
            if (operation == "status")
            {
                if (current == null)
                {
                    return McpHostResult.Success(new { state = "idle" });
                }
                var requested = arguments["runId"]?.Value<string>();
                if (!string.IsNullOrEmpty(requested) && requested != (string)current["runId"])
                {
                    return McpHostResult.Fail("runId is not the latest observed run. Use the persisted resultPath from an earlier response.", -32602);
                }
                return McpHostResult.Success(current.DeepClone());
            }
            var assembly = arguments["assemblyName"]?.Value<string>();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return McpHostResult.Fail("Exit Play Mode before running EditMode tests. Status remains readable.");
            }
            if (string.IsNullOrWhiteSpace(assembly))
            {
                throw new ArgumentException("assemblyName must be nonempty.");
            }
            if (ownsRun || McpEditModeTestRunner.Instance.IsRunning)
            {
                return McpHostResult.Fail("A test run is already active.");
            }
            if (!Array.Exists(AppDomain.CurrentDomain.GetAssemblies(), item => item.GetName().Name == assembly))
            {
                return McpHostResult.Fail("Test assembly is not loaded. Check testables/asmdef and compilation: " + assembly, -32602);
            }
            var id = Guid.NewGuid().ToString("N");
            current = new JObject { ["schema"] = "kabu.tests.run/v1", ["runId"] = id, ["state"] = "queued",
                ["assemblyName"] = assembly, ["startedUtc"] = DateTime.UtcNow.ToString("o"),
                ["resultPath"] = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/KabuUnityMcp/TestRuns", id + ".json")),
                ["cancellationSupported"] = false };
            failures.Clear();
            ownsRun = true;
            Persist();
            var filter = new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { assembly } };
            var testName = arguments["testName"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(testName))
            {
                filter.testNames = new[] { testName };
            }
            EditorApplication.delayCall += () => Start(filter);
            return McpHostResult.Success(current.DeepClone());
        }

        private void Start(Filter filter)
        {
            try
            {
                api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(this);
                current["state"] = "running";
                Persist();
                api.Execute(new ExecutionSettings(filter));
            }
            catch (Exception exception)
            {
                current["state"] = "failed";
                current["message"] = exception.GetBaseException().Message;
                Finish();
            }
        }

        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (ownsRun && !result.Test.IsSuite && result.ResultState.StartsWith("Failed", StringComparison.Ordinal) && failures.Count < 200)
            {
                failures.Add(new { test = result.FullName, message = Truncate(result.Message), stackTrace = Truncate(result.StackTrace) });
            }
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (!ownsRun)
            {
                return;
            }
            current["state"] = result.FailCount > 0 || result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount == 0 ? "failed" : "completed";
            current["passed"] = result.PassCount;
            current["failed"] = result.FailCount;
            current["skipped"] = result.SkipCount;
            current["inconclusive"] = result.InconclusiveCount;
            current["durationSeconds"] = result.Duration;
            current["failures"] = JArray.FromObject(failures);
            Finish();
        }

        private static string Truncate(string value) => value?.Substring(0, Math.Min(value.Length, 4000));

        private void Finish()
        {
            ownsRun = false;
            current["finishedUtc"] = DateTime.UtcNow.ToString("o");
            Persist();
            if (api)
            {
                var completedApi = api;
                api = null;
                EditorApplication.delayCall += () =>
                {
                    if (completedApi)
                    {
                        completedApi.UnregisterCallbacks(this);
                        UnityEngine.Object.DestroyImmediate(completedApi);
                    }
                };
            }
        }

        private void Persist()
        {
            SessionState.SetString(SessionKey, current.ToString());
            var path = (string)current["resultPath"];
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, current.ToString());
        }

        private void BeforeReload()
        {
            if (api)
            {
                api.UnregisterCallbacks(this);
            }
        }
    }
}
