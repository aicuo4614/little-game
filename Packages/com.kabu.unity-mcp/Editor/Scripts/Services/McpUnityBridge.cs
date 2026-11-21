using System;
using System.Threading;
using UnityEditor.Compilation;
using KabuUnityMcp.Editor.Extensions;
using KabuUnityMcp.Editor.Handlers.Assets;
using KabuUnityMcp.Editor.Handlers.Components;
using KabuUnityMcp.Editor.Handlers.Console;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using KabuUnityMcp.Editor.Handlers.Prefabs;
using KabuUnityMcp.Editor.Handlers.Profiler;
using KabuUnityMcp.Editor.Handlers.Scenes;
using KabuUnityMcp.Editor.Handlers.SystemTools;
using KabuUnityMcp.Editor.Utils;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Services
{
    /// <summary>
    /// Owns the Runtime WebSocket client. Dispatches create/list commands to handlers.
    /// Survives Play Mode / script Domain Reload via SessionState + auto-reconnect.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpUnityBridge
    {
        private const string WantConnectedKey = "KabuUnityMcp.WsWantConnected";
        private const string ReconnectAttemptKey = "KabuUnityMcp.WsReconnectAttempt";
        /// <summary>本 Editor 进程内是否已做过一次冷启动自动连接（Domain Reload 不重复）。</summary>
        private const string ColdStartConnectAttemptedKey = "KabuUnityMcp.ColdStartConnectAttempted";
        private const int MaxAutoReconnectAttempts = 5;
        private const double AutoReconnectDelaySeconds = 2.0;
        private const double WatchPortIntervalSeconds = 15.0;

        private static McpWebSocketClient _client;
        private static bool _busy;
        private static bool _ready;
        private static CancellationTokenSource _connectCancellation;
        private static readonly System.Random RetryRandom = new System.Random();
        private static bool _autoReconnectPending;
        private static double _autoReconnectAt;
        private static bool _watchingForPort;
        private static string _statusDetail;

        public static bool IsConnected => _ready && _client != null && _client.IsConnected;
        public static bool IsBusy => _busy;
        public static string StatusText
        {
            get
            {
                if (EditorApplication.isCompiling) return "Compiling — connection will recover after reload";
                if (IsConnected)
                    return $"Connected — {McpServerEndpoints.WsUrl}";
                if (!string.IsNullOrEmpty(_statusDetail))
                    return _statusDetail;
                if (_busy)
                    return "Connecting…";
                if (_watchingForPort && GetWantConnected())
                    return "Waiting for server… (will auto-connect when ready)";
                if (_autoReconnectPending)
                    return $"Waiting to reconnect — attempt {SessionState.GetInt(ReconnectAttemptKey, 0) + 1}, in {Math.Max(0, _autoReconnectAt - EditorApplication.timeSinceStartup):0}s";
                return "Disconnected";
            }
        }

        public static event Action Changed;

        static McpUnityBridge()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += () => { SetWantConnected(false); _connectCancellation?.Cancel(); _client?.Dispose(); };
            CompilationPipeline.compilationStarted += _ => ReportEditorState("compiling");
            CompilationPipeline.compilationFinished += _ => ReportEditorState("ready");

            // Play Mode / compile Domain Reload clears statics and the socket;
            // restore the connection if the user (or a prior session) had connected.
            if (GetWantConnected())
            {
                Debug.Log("[kabu-unity-mcp] Domain reload detected — scheduling WebSocket auto-reconnect.");
                ScheduleAutoReconnect(immediate: true);
                return;
            }

            TryScheduleColdStartConnect();
        }

        /// <summary>
        /// 编辑器冷启动：settings.autoConnectOnEditorStart 为 true 且尚未连接时，
        /// 自动 EnsureRunning + WebSocket 一次（本进程只试一次，失败后仍可走重试/手动连接）。
        /// </summary>
        private static void TryScheduleColdStartConnect()
        {
            if (SessionState.GetBool(ColdStartConnectAttemptedKey, false))
            {
                return;
            }

            if (!McpProjectSettingsStore.IsAutoConnectOnEditorStart())
            {
                return;
            }

            SessionState.SetBool(ColdStartConnectAttemptedKey, true);
            SetWantConnected(true);
            ScheduleAutoReconnect(immediate: true);
        }

        private static async void ReportEditorState(string state)
        {
            if (_client == null || !_client.IsConnected) return;
            try { await _client.SendNotificationAsync("unity.editorState", new { state }); }
            catch (Exception ex) { SetStatusDetail(ex.Message); }
        }

        /// <summary>
        /// 一键：必要时启动 Server → 等 /health → WebSocket 连接。
        /// 手动点击「连接」时调用；会重置自动重连计数，失败弹窗。
        /// </summary>
        public static void EnsureConnected()
        {
            ConnectCore(interactive: true, resetAttempts: true);
        }

        /// <summary>
        /// 自动重连入口：与「连接」相同路径（必要时启服），但不弹窗、不刷屏。
        /// </summary>
        public static void Connect()
        {
            ConnectCore(interactive: false, resetAttempts: false);
        }

        /// <summary>
        /// 统一连接：EnsureRunning（启服/等就绪）→ WebSocket。
        /// 以前自动重连只等端口 15s 且不启服，冷启动/Domain Reload 时会频繁打
        /// 「Server port not ready」且连不上。
        /// </summary>
        private static async void ConnectCore(bool interactive, bool resetAttempts)
        {
            if (IsConnected || _busy)
                return;

            SetWantConnected(true);
            if (resetAttempts)
            {
                SessionState.SetInt(ReconnectAttemptKey, 0);
                _autoReconnectPending = false;
            }

            _watchingForPort = false;
            _busy = true;
            SetStatusDetail(interactive ? "Ensuring server…" : "Connecting…");
            RaiseChanged();

            var ok = false;
            var cancellation = new CancellationTokenSource();
            _connectCancellation = cancellation;
            try
            {
                SetStatusDetail("Ensuring server…");
                RaiseChanged();

                var ensure = await McpServerLauncher.EnsureRunningAsync(cancellationToken: cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!ensure.Ok)
                {
                    SetStatusDetail(interactive ? null : "Waiting for server…");
                    if (interactive)
                    {
                        Debug.LogError($"[kabu-unity-mcp] EnsureConnected failed: {ensure.Error}");
                        EditorUtility.DisplayDialog("kabu-unity-mcp", ensure.Error, "OK");
                    }
                    else
                    {
                        // 自动重连：只更新状态，避免每次 Domain Reload 刷一条 Warning
                        Debug.Log(
                            $"[kabu-unity-mcp] Server not ready yet ({ensure.Error}). Will retry while WantConnected.");
                    }

                    return;
                }

                SetStatusDetail("Connecting WebSocket…");
                RaiseChanged();

                EnsureClient();
                var transportConnected = await _client.ConnectAsync();
                cancellation.Token.ThrowIfCancellationRequested();
                ok = transportConnected;
                if (ok)
                {
                    _watchingForPort = false;
                    SetStatusDetail("Registering editor…");
                    ok = false;
                    await SendRegisterAsync();
                    await _client.WaitForRegistrationAsync();
                    cancellation.Token.ThrowIfCancellationRequested();
                    _ready = true;
                    SessionState.SetInt(ReconnectAttemptKey, 0);
                    SetStatusDetail(null);
                    ok = true;
                }
                else
                {
                    SetStatusDetail(null);
                    if (interactive)
                        Debug.LogWarning("[kabu-unity-mcp] WebSocket connect failed after server ready.");
                }
            }
            catch (Exception ex)
            {
                SetStatusDetail(ex.Message);
                if (interactive)
                    Debug.LogError($"[kabu-unity-mcp] EnsureConnected error: {ex.Message}");
                else
                    Debug.Log($"[kabu-unity-mcp] Connect error: {ex.Message}");
            }
            finally
            {
                if (!ok && !interactive && string.IsNullOrEmpty(_statusDetail))
                    SetStatusDetail(null);
                if (!ok) { _ready = false; _client?.Dispose(); }
                if (ReferenceEquals(_connectCancellation, cancellation)) _connectCancellation = null;
                cancellation.Dispose();
                _busy = false;
                RaiseChanged();
                if (!ok && GetWantConnected())
                    ScheduleAutoReconnect(immediate: false);
            }
        }

        /// <summary>Push current enabled-tool list to Server (after toggling in UI).</summary>
        public static async void PushToolPreferences()
        {
            if (!IsConnected || _client == null)
                return;

            try
            {
                await _client.SendNotificationAsync("unity.setToolPreferences", new
                {
                    enabledTools = McpProjectSettingsStore.GetEnabledToolNames()
                });
                Debug.Log("[kabu-unity-mcp] Tool preferences synced to Server.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[kabu-unity-mcp] sync tools failed: {ex.Message}");
            }
        }

        private static async System.Threading.Tasks.Task SendRegisterAsync()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            await _client.SendNotificationAsync("unity.register", new
            {
                editorId = GetEditorId(),
                projectName = Application.productName,
                unityVersion = Application.unityVersion,
                machineName = Environment.MachineName,
                sceneName = scene.name,
                scenePath = scene.path,
                dataPath = Application.dataPath,
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                isCompiling = EditorApplication.isCompiling,
                isPlaying = EditorApplication.isPlaying,
                enabledTools = McpProjectSettingsStore.GetEnabledToolNames()
            });
        }

        public static void Disconnect() => _ = DisconnectAsync();

        /// <summary>Marks the socket as an intentional close so a dropped handshake is not an error.</summary>
        public static void PrepareForServerStop() => _client?.MarkClosing();

        public static async System.Threading.Tasks.Task DisconnectAsync()
        {
            SetWantConnected(false);
            _ready = false;
            _connectCancellation?.Cancel();
            SessionState.SetInt(ReconnectAttemptKey, 0);
            _autoReconnectPending = false;
            _watchingForPort = false;
            SetStatusDetail(null);

            if (_client == null)
                return;

            _busy = true;
            RaiseChanged();
            try
            {
                _client.MarkClosing();
                await _client.DisconnectAsync();
            }
            finally
            {
                _busy = _connectCancellation != null;
                RaiseChanged();
            }
        }

        private static void EnsureClient()
        {
            var wsUrl = McpServerEndpoints.WsUrl + "?editorId=" + GetEditorId();
            if (_client != null && _client.Url == wsUrl && _client.IsConnected)
                return;

            if (_client != null)
            {
                try { _client.Dispose(); } catch { /* ignore */ }
                _client = null;
            }

            _client = new McpWebSocketClient(wsUrl);
            _client.OnConnected += () =>
            {
                Debug.Log("[kabu-unity-mcp] Bridge connected.");
                RaiseChanged();
            };
            _client.OnDisconnected += reason =>
            {
                _ready = false;
                SetStatusDetail(reason);
                Debug.Log($"[kabu-unity-mcp] Bridge disconnected: {reason}");
                RaiseChanged();
                // 服务进程重启或 SSE/WS 被对端关掉时，只要用户还想连着就自己拉起来。
                if (GetWantConnected())
                    ScheduleAutoReconnect(immediate: false);
            };
            _client.OnError += err =>
            {
                Debug.LogError($"[kabu-unity-mcp] Bridge error: {err}");
                RaiseChanged();
            };
            _client.OnNotification += OnNotification;
            _client.OnRequest += OnRequest;
        }

        private static void OnNotification(string method, JToken parameters)
        {
            Debug.Log($"[kabu-unity-mcp] Unhandled notification: {method}");
        }

        private static void OnRequest(string requestId, string method, JToken parameters)
        {
            if (McpMethodRegistry.TryDispatch(method, requestId, parameters, _client))
            {
                return;
            }

            switch (method)
            {
                case "unity.createGameObject":
                    GameObjectHandlers.Create(requestId, parameters, _client);
                    break;
                case "unity.listSceneObjects":
                    GameObjectHandlers.List(requestId, parameters, _client);
                    break;
                case "unity.findGameObject":
                    GameObjectHandlers.Find(requestId, parameters, _client);
                    break;
                case "unity.destroyGameObject":
                    GameObjectHandlers.Destroy(requestId, parameters, _client);
                    break;
                case "unity.duplicateGameObject":
                    GameObjectHandlers.Duplicate(requestId, parameters, _client);
                    break;
                case "unity.setParent":
                    GameObjectHandlers.SetParent(requestId, parameters, _client);
                    break;
                case "unity.modifyGameObject":
                    GameObjectHandlers.Modify(requestId, parameters, _client);
                    break;
                case "unity.listOpenedScenes":
                    SceneHandlers.ListOpened(requestId, _client);
                    break;
                case "unity.getSceneData":
                    SceneHandlers.GetData(requestId, parameters, _client);
                    break;
                case "unity.openScene":
                    SceneHandlers.Open(requestId, parameters, _client);
                    break;
                case "unity.saveScene":
                    SceneHandlers.Save(requestId, parameters, _client);
                    break;
                case "unity.findAssets":
                    AssetHandlers.Find(requestId, parameters, _client);
                    break;
                case "unity.refreshAssets":
                    AssetHandlers.Refresh(requestId, _client);
                    break;
                case "unity.createFolder":
                    AssetHandlers.CreateFolder(requestId, parameters, _client);
                    break;
                case "unity.copyAsset":
                    AssetHandlers.Copy(requestId, parameters, _client);
                    break;
                case "unity.moveAsset":
                    AssetHandlers.Move(requestId, parameters, _client);
                    break;
                case "unity.deleteAsset":
                    AssetHandlers.Delete(requestId, parameters, _client);
                    break;
                case "unity.getConsoleLogs":
                    ConsoleHandlers.GetLogs(requestId, parameters, _client);
                    break;
                case "unity.clearConsoleLogs":
                    ConsoleHandlers.ClearLogs(requestId, _client);
                    break;
                case "unity.addComponent":
                    ComponentHandlers.Add(requestId, parameters, _client);
                    break;
                case "unity.removeComponent":
                    ComponentHandlers.Remove(requestId, parameters, _client);
                    break;
                case "unity.setComponentField":
                    ComponentHandlers.SetField(requestId, parameters, _client);
                    break;
                case "unity.createPrefab":
                    PrefabHandlers.Create(requestId, parameters, _client);
                    break;
                case "unity.instantiatePrefab":
                    PrefabHandlers.Instantiate(requestId, parameters, _client);
                    break;
                case "unity.getPrefabInfo":
                    PrefabHandlers.GetInfo(requestId, parameters, _client);
                    break;
                case "unity.createScript":
                    AssetHandlers.CreateScript(requestId, parameters, _client);
                    break;
                case "unity.createAsset":
                    AssetHandlers.CreateAsset(requestId, parameters, _client);
                    break;
                case "unity.modelListAnimationClips":
                    ModelAnimationHandlers.ListClips(requestId, parameters, _client);
                    break;
                case "unity.modelGetAnimationClip":
                    ModelAnimationHandlers.GetClip(requestId, parameters, _client);
                    break;
                case "unity.modelSetAnimationClip":
                    ModelAnimationHandlers.SetClip(requestId, parameters, _client);
                    break;
                case "unity.openPrefab":
                    PrefabHandlers.OpenStage(requestId, parameters, _client);
                    break;
                case "unity.savePrefab":
                    PrefabHandlers.Save(requestId, parameters, _client);
                    break;
                case "unity.closePrefabStage":
                    PrefabHandlers.CloseStage(requestId, parameters, _client);
                    break;
                case "unity.getPrefabStage":
                    PrefabHandlers.GetStageInfo(requestId, _client);
                    break;
                case "unity.getActiveScene":
                    SceneHandlers.GetActive(requestId, _client);
                    break;
                case "unity.listScenes":
                    SceneHandlers.ListProject(requestId, parameters, _client);
                    break;
                case "unity.setActiveScene":
                    SceneHandlers.SetActive(requestId, parameters, _client);
                    break;
                case "unity.closeScene":
                    SceneHandlers.Close(requestId, parameters, _client);
                    break;
                case "unity.getProjectInfo":
                    SystemHandlers.GetProjectInfo(requestId, _client);
                    break;
                case "unity.getPlayModeState":
                    SystemHandlers.GetPlayModeState(requestId, _client);
                    break;
                case "unity.enterPlayMode":
                    SystemHandlers.EnterPlayMode(requestId, parameters, _client);
                    break;
                case "unity.exitPlayMode":
                    SystemHandlers.ExitPlayMode(requestId, _client);
                    break;
                case "unity.stepPlayModeFrame":
                    SystemHandlers.StepPlayModeFrame(requestId, _client);
                    break;
                case "unity.getCompilationStatus":
                    SystemHandlers.GetCompilationStatus(requestId, _client);
                    break;
                case "unity.runMenuItem":
                    SystemHandlers.RunMenuItem(requestId, parameters, _client);
                    break;
                case "unity.triggerCompilation":
                    SystemHandlers.TriggerCompilation(requestId, _client);
                    break;
                case "unity.bringToForeground":
                    SystemHandlers.BringToForeground(requestId, _client);
                    break;
                case "unity.batchCreateGameObjects":
                    GameObjectHandlers.BatchCreate(requestId, parameters, _client);
                    break;
                case "unity.createGameObjectInScene":
                    GameObjectHandlers.CreateInScene(requestId, parameters, _client);
                    break;
                case "unity.frameDebuggerEnable":
                    FrameDebuggerHandlers.Enable(requestId, _client);
                    break;
                case "unity.frameDebuggerDisable":
                    FrameDebuggerHandlers.Disable(requestId, _client);
                    break;
                case "unity.frameDebuggerList":
                    FrameDebuggerHandlers.List(requestId, parameters, _client);
                    break;
                case "unity.frameDebuggerGetEvent":
                    FrameDebuggerHandlers.GetEvent(requestId, parameters, _client);
                    break;
                case "unity.profilerEnable":
                    ProfilerHandlers.Enable(requestId, parameters, _client);
                    break;
                case "unity.profilerDisable":
                    ProfilerHandlers.Disable(requestId, _client);
                    break;
                case "unity.profilerStatus":
                    ProfilerHandlers.Status(requestId, _client);
                    break;
                case "unity.profilerSetAreas":
                    ProfilerHandlers.SetAreas(requestId, parameters, _client);
                    break;
                case "unity.profilerClear":
                    ProfilerHandlers.Clear(requestId, _client);
                    break;
                case "unity.profilerGetFrameTiming":
                    ProfilerHandlers.GetFrameTiming(requestId, _client);
                    break;
                case "unity.profilerListCounters":
                    ProfilerHandlers.ListCounters(requestId, parameters, _client);
                    break;
                case "unity.profilerGetCounters":
                    ProfilerHandlers.GetCounters(requestId, parameters, _client);
                    break;
                case "unity.profilerGetHierarchy":
                    ProfilerHandlers.GetHierarchy(requestId, parameters, _client);
                    break;
                case "unity.profilerGetHierarchyChildren":
                    ProfilerHandlers.GetHierarchyChildren(requestId, parameters, _client);
                    break;
                case "unity.profilerGetObjectMemory":
                    ProfilerHandlers.GetObjectMemory(requestId, parameters, _client);
                    break;
                case "unity.profilerMemoryTargets":
                    ProfilerHandlers.MemoryTargets(requestId, parameters, _client);
                    break;
                case "unity.profilerMemoryTakeSnapshot":
                    ProfilerHandlers.MemoryTakeSnapshot(requestId, parameters, _client);
                    break;
                case "unity.profilerMemoryListSnapshots":
                    ProfilerHandlers.MemoryListSnapshots(requestId, parameters, _client);
                    break;
                case "unity.profilerMemoryCompareSnapshots":
                    ProfilerHandlers.MemoryCompareSnapshots(requestId, parameters, _client);
                    break;
                case "unity.perfCaptureFrame":
                    PerfCaptureHandlers.Capture(requestId, parameters, _client);
                    break;
                case "unity.perfRendererSweep":
                    PerfRendererSweepHandlers.Sweep(requestId, parameters, _client);
                    break;
                case "unity.perfGetOverdraw":
                    PerfEvidenceHandlers.GetOverdraw(requestId, parameters, _client);
                    break;
                case "unity.getOcclusionInfo":
                    PerfEvidenceHandlers.GetOcclusionInfo(requestId, _client);
                    break;
                case "unity.getPostProcessStack":
                    PerfEvidenceHandlers.GetPostProcessStack(requestId, parameters, _client);
                    break;
                case "unity.getRenderCensus":
                    PerfEvidenceHandlers.GetRenderCensus(requestId, _client);
                    break;
                case "unity.gpuListBuffers":
                    GpuBufferHandlers.List(requestId, parameters, _client);
                    break;
                case "unity.gpuReadBuffer":
                    GpuBufferHandlers.Read(requestId, parameters, _client);
                    break;
                default:
                    Debug.LogWarning($"[kabu-unity-mcp] Unhandled request: {method}");
                    _ = _client.SendErrorAsync(requestId, -32601, $"Unknown method: {method}");
                    break;
            }
        }

        private static void Tick()
        {
            _client?.ProcessMainThreadQueue();

            if (IsConnected || _busy || !GetWantConnected())
            {
                if (!GetWantConnected())
                {
                    _autoReconnectPending = false;
                    _watchingForPort = false;
                }
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            // After burst of failed attempts: keep trying EnsureRunning+WS on an interval
            // (not only IsPortOpen — otherwise Server 挂了永远不会被自动拉起)。
            if (_watchingForPort)
            {
                if (EditorApplication.timeSinceStartup < _autoReconnectAt)
                    return;

                _autoReconnectAt = EditorApplication.timeSinceStartup + WatchPortIntervalSeconds;
                SessionState.SetInt(ReconnectAttemptKey, 0);
                _watchingForPort = false;
                Debug.Log("[kabu-unity-mcp] Retrying server ensure + WebSocket connect…");
                Connect();
                return;
            }

            if (!_autoReconnectPending)
                return;
            if (EditorApplication.timeSinceStartup < _autoReconnectAt)
                return;

            _autoReconnectPending = false;
            var attempt = SessionState.GetInt(ReconnectAttemptKey, 0);
            if (attempt >= MaxAutoReconnectAttempts)
            {
                _watchingForPort = true;
                _autoReconnectAt = EditorApplication.timeSinceStartup + WatchPortIntervalSeconds;
                SetStatusDetail("Waiting for server… (auto-retry)");
                Debug.Log(
                    $"[kabu-unity-mcp] Auto-reconnect paused after {MaxAutoReconnectAttempts} attempts; " +
                    $"will retry ensure+connect every {WatchPortIntervalSeconds:0}s. " +
                    "Or use Server Window → 连接.");
                RaiseChanged();
                return;
            }

            SessionState.SetInt(ReconnectAttemptKey, attempt + 1);
            Debug.Log($"[kabu-unity-mcp] Auto-reconnect attempt {attempt + 1}/{MaxAutoReconnectAttempts}…");
            Connect();
        }

        private static void OnBeforeReload()
        {
            _connectCancellation?.Cancel();
            _ready = false;
            // Persist intent before statics are wiped. Connected or in-flight both count.
            if (IsConnected || _busy || GetWantConnected())
            {
                SetWantConnected(true);
                SessionState.SetInt(ReconnectAttemptKey, 0);
            }

            if (_client == null)
                return;
            try
            {
                _client.MarkClosing();
                _client.Dispose();
            }
            catch { /* ignore */ }
            _client = null;
        }

        private static void ScheduleAutoReconnect(bool immediate)
        {
            if (!GetWantConnected())
                return;
            _autoReconnectPending = true;
            _watchingForPort = false;
            _autoReconnectAt = immediate
                ? 0
                : EditorApplication.timeSinceStartup + Math.Min(30, AutoReconnectDelaySeconds * Math.Pow(2, SessionState.GetInt(ReconnectAttemptKey, 0))) + RetryRandom.NextDouble();
        }

        private static string GetEditorId()
        {
            const string key = "KabuUnityMcp.EditorInstanceId";
            var id = SessionState.GetString(key, "");
            if (string.IsNullOrEmpty(id)) { id = Guid.NewGuid().ToString("N"); SessionState.SetString(key, id); }
            return id;
        }

        private static bool GetWantConnected() =>
            SessionState.GetBool(WantConnectedKey, false);

        private static void SetWantConnected(bool value) =>
            SessionState.SetBool(WantConnectedKey, value);

        private static void SetStatusDetail(string detail) => _statusDetail = detail;

        private static void RaiseChanged()
        {
            EditorApplication.delayCall += () => Changed?.Invoke();
        }
    }
}
