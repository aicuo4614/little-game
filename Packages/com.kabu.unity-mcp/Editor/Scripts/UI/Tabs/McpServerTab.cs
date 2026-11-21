using KabuUnityMcp.Editor.Models;
using KabuUnityMcp.Editor.Services;
using KabuUnityMcp.Editor.Utils;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.UI.Tabs
{
    /// <summary>服务器：端口、进程启停、WebSocket 连接。</summary>
    internal sealed class McpServerTab
    {
        private int _portDraft;
        private bool _portDraftReady;

        public void OnEnable()
        {
            _portDraft = McpServerEndpoints.Port;
            _portDraftReady = true;
        }

        public void Draw()
        {
            var status = McpServerStatusService.Instance.Current;
            if (!_portDraftReady)
            {
                _portDraft = McpServerEndpoints.Port;
                _portDraftReady = true;
            }

            DrawProjectSettings(status);
            EditorGUILayout.Space(10);
            DrawServerProcess(status);
            EditorGUILayout.Space(14);
            DrawWebSocket(status);

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "端口与工具开关保存在本工程 Assets/kabu-unity-mcp/settings.json。\n" +
                "改工具开关后若已连接，会自动同步到 Server（Agent 需重新拉工具列表）。",
                MessageType.Info);
        }

        private void DrawProjectSettings(McpServerStatusSnapshot status)
        {
            EditorGUILayout.LabelField("工程配置（settings.json）", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(status.IsRunning || McpUnityBridge.IsConnected || McpUnityBridge.IsBusy);
            EditorGUI.BeginChangeCheck();
            _portDraft = EditorGUILayout.IntField("端口", _portDraft);
            if (EditorGUI.EndChangeCheck())
                _portDraft = Mathf.Clamp(_portDraft, 1024, 65535);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                var dirty = _portDraft != McpServerEndpoints.Port;
                GUI.enabled = dirty && !status.IsRunning && !McpUnityBridge.IsConnected;
                if (GUILayout.Button("保存端口", GUILayout.Width(88), GUILayout.Height(22)))
                {
                    McpServerEndpoints.SetPort(_portDraft);
                    McpServerStatusService.Instance.RequestRefresh();
                    Debug.Log($"[kabu-unity-mcp] 端口已保存 → {McpProjectSettingsStore.JsonPath}");
                }

                GUI.enabled = true;
            }

            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginChangeCheck();
            var autoConnect = EditorGUILayout.Toggle(
                new GUIContent(
                    "启动时自动连接",
                    "Unity 冷启动时若未连接，自动启动 MCP Server 并连一次 WebSocket。"),
                McpProjectSettingsStore.IsAutoConnectOnEditorStart());
            if (EditorGUI.EndChangeCheck())
                McpProjectSettingsStore.SetAutoConnectOnEditorStart(autoConnect);

            EditorGUILayout.LabelField("配置文件", McpProjectSettingsStore.JsonPath);
            if (status.IsRunning || McpUnityBridge.IsConnected)
                EditorGUILayout.HelpBox("改端口前请先关闭服务器并断开 WebSocket。", MessageType.None);
        }

        private void DrawServerProcess(McpServerStatusSnapshot status)
        {
            EditorGUILayout.LabelField("MCP Server 进程", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", status.StatusText);
            EditorGUILayout.LabelField("HTTP", McpServerEndpoints.McpUrl);

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                var running = status.IsRunning;
                var startLabel = status.State == McpServerRunState.RunningExternal
                    ? "重启服务器（回收端口）"
                    : running
                        ? "重启服务器"
                        : "启动服务器";
                GUI.enabled = !McpUnityBridge.IsBusy;
                if (GUILayout.Button(startLabel, GUILayout.Height(28)))
                {
                    if (_portDraft != McpServerEndpoints.Port)
                        McpServerEndpoints.SetPort(_portDraft);
                    var result = McpServerLauncher.Start();
                    if (!result.Ok)
                        EditorUtility.DisplayDialog("kabu-unity-mcp", result.Error, "OK");
                }

                GUI.enabled = running && !McpUnityBridge.IsBusy;
                if (GUILayout.Button("关闭服务器", GUILayout.Height(28)))
                    McpServerLauncher.Stop();
                GUI.enabled = true;
            }

            if (status.State == McpServerRunState.Unknown)
                EditorGUILayout.HelpBox("正在检测服务器状态…", MessageType.None);
            else if (status.IsRunning)
                EditorGUILayout.HelpBox(
                    "服务器已在运行。「启动/重启」会先回收端口再拉起；日常请用「关闭服务器」。",
                    MessageType.None);
        }

        private void DrawWebSocket(McpServerStatusSnapshot status)
        {
            EditorGUILayout.LabelField("Unity ↔ Server (WebSocket)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", McpUnityBridge.StatusText);
            EditorGUILayout.LabelField("WS", McpServerEndpoints.WsUrl);

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                // 不依赖 status.IsRunning：点「连接」会自动启服并等到就绪
                GUI.enabled = !McpUnityBridge.IsConnected && !McpUnityBridge.IsBusy;
                if (GUILayout.Button("连接", GUILayout.Height(28)))
                {
                    if (_portDraft != McpServerEndpoints.Port)
                        McpServerEndpoints.SetPort(_portDraft);
                    McpUnityBridge.EnsureConnected();
                }

                GUI.enabled = McpUnityBridge.IsConnected && !McpUnityBridge.IsBusy;
                if (GUILayout.Button("断开", GUILayout.Height(28)))
                    McpUnityBridge.Disconnect();
                GUI.enabled = true;
            }

            EditorGUILayout.HelpBox(
                "「连接」会在需要时自动启动 Server、等待 /health 就绪，再连 WebSocket。\n" +
                "勾选「启动时自动连接」后，Unity 冷启动会自动启服并连一次（本进程只自动发起一次）。\n" +
                "Domain Reload / Play 切换后会自动重连；若 Server 未在跑也会尝试拉起（与点「连接」同路径）。\n" +
                "冷启动 dotnet 可能要几十秒，状态栏显示 Ensuring server… 时请稍等，不必连点。",
                MessageType.None);
        }
    }
}
