using KabuUnityMcp.Editor.Handlers.Profiler;
using KabuUnityMcp.Editor.Services;
using KabuUnityMcp.Editor.UI.Tabs;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.UI
{
    /// <summary>
    /// Shell window: lifecycle + tab bar. Tab content lives under <see cref="Tabs"/>.
    /// </summary>
    public class McpServerWindow : EditorWindow
    {
        private static readonly string[] TabLabels = { "服务器", "MCP 工具", "配置", "测试" };

        private int _tab;
        private readonly McpServerTab _serverTab = new McpServerTab();
        private readonly McpToolsTab _toolsTab = new McpToolsTab();
        private readonly McpConfigTab _configTab = new McpConfigTab();
        private readonly McpTestTab _testTab = new McpTestTab();

        [MenuItem("Kabu/kabu-unity-mcp/Server Window")]
        public static void Open()
        {
            var window = GetWindow<McpServerWindow>();
            window.titleContent = new GUIContent("kabu-unity-mcp");
            window.minSize = new Vector2(420, 360);
            window.Show();
        }

        private void OnEnable()
        {
            McpServerStatusService.Instance.Retain();
            McpServerStatusService.Instance.Changed += OnStatusChanged;
            McpUnityBridge.Changed += OnStatusChanged;
            McpEditModeTestRunner.Instance.Changed += OnStatusChanged;
            PerfCaptureHandlers.Changed += OnStatusChanged;
            OverdrawAbHandlers.Changed += OnStatusChanged;
            _ = McpProjectSettingsStore.GetOrCreate();
            _serverTab.OnEnable();
            _testTab.OnEnable();
        }

        private void OnDisable()
        {
            McpServerStatusService.Instance.Changed -= OnStatusChanged;
            McpServerStatusService.Instance.Release();
            McpUnityBridge.Changed -= OnStatusChanged;
            if (McpEditModeTestRunner.Instance != null)
                McpEditModeTestRunner.Instance.Changed -= OnStatusChanged;
            PerfCaptureHandlers.Changed -= OnStatusChanged;
            OverdrawAbHandlers.Changed -= OnStatusChanged;
            _testTab.OnDisable();
        }

        private void OnStatusChanged() => Repaint();

        private void OnInspectorUpdate()
        {
            if (_tab == 3) Repaint();
        }

        private void OnGUI()
        {
            _tab = GUILayout.Toolbar(_tab, TabLabels);
            EditorGUILayout.Space(6);

            switch (_tab)
            {
                case 0: _serverTab.Draw(); break;
                case 1: _toolsTab.Draw(); break;
                case 2: _configTab.Draw(); break;
                case 3: _testTab.Draw(); break;
            }
        }
    }
}
