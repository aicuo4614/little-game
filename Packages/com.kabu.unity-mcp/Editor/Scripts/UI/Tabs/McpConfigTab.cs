using KabuUnityMcp.Editor.Services;
using KabuUnityMcp.Editor.Utils;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.UI.Tabs
{
    /// <summary>配置：Cursor mcp.json、Codex config.toml 与快捷复制。</summary>
    internal sealed class McpConfigTab
    {
        private Vector2 _scroll;
        private GUIStyle _jsonStyle;
        private GUIStyle _tomlStyle;

        public void Draw()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("客户端配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "URL 随「服务器」页端口变化。改端口后请同步 Cursor / AILearning 的 mcp.json，以及 Codex 的 config.toml。",
                MessageType.Info);

            EditorGUILayout.Space(8);
            McpEditorGui.CopyableJson("Cursor / AILearning mcp.json",
                McpClientConfigText.BuildMcpJson(), ref _jsonStyle);

            EditorGUILayout.Space(12);
            EditorGUILayout.HelpBox(
                "Codex 写入 ~/.codex/config.toml，或工程根目录 .codex/config.toml（仅 trusted 项目）。HTTP MCP 需 [features] rmcp_client = true。",
                MessageType.Info);
            McpEditorGui.CopyableSnippet("Codex config.toml",
                McpClientConfigText.BuildCodexToml(), "复制 TOML", ref _tomlStyle);

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("快捷信息", EditorStyles.boldLabel);
            McpEditorGui.CopyRow("MCP URL", McpServerEndpoints.McpUrl);
            McpEditorGui.CopyRow("WebSocket", McpServerEndpoints.WsUrl);
            McpEditorGui.CopyRow("Health", McpServerEndpoints.HealthUrl);
            McpEditorGui.CopyRow("Port", McpServerEndpoints.Port.ToString());
            McpEditorGui.CopyRow("settings.json", McpProjectSettingsStore.JsonPath);

            EditorGUILayout.EndScrollView();
        }
    }
}
