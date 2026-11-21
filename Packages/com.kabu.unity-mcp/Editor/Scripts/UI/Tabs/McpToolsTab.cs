using System.Linq;
using KabuUnityMcp.Editor.Models;
using KabuUnityMcp.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.UI.Tabs
{
    /// <summary>MCP 工具：分类卡片列表 + 开关。</summary>
    internal sealed class McpToolsTab
    {
        private Vector2 _scroll;
        private GUIStyle _nameStyle;
        private GUIStyle _descStyle;
        private GUIStyle _categoryStyle;

        public void Draw()
        {
            EnsureStyles();
            DrawHeader();
            DrawBulkActions();

            EditorGUILayout.Space(8);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawToolList();
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            var onCount = McpToolCatalog.All.Count(t => McpProjectSettingsStore.IsToolEnabled(t.Name));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("MCP 工具", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{onCount} / {McpToolCatalog.All.Length} 已启用",
                    EditorStyles.miniLabel, GUILayout.Width(110));
            }

            EditorGUILayout.HelpBox(
                "关闭后 Agent 的 tools/list 将看不到该工具。设置写入 settings.json。",
                MessageType.None);
        }

        private static void DrawBulkActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全部开启", GUILayout.Height(24)))
                    SetAll(true);

                if (GUILayout.Button("全部关闭", GUILayout.Height(24)))
                    SetAll(false);
            }
        }

        private static void SetAll(bool enabled)
        {
            foreach (var t in McpToolCatalog.All)
                McpProjectSettingsStore.SetToolEnabled(t.Name, enabled);
            McpUnityBridge.PushToolPreferences();
        }

        private void DrawToolList()
        {
            string lastCategory = null;
            var firstCategory = McpToolCatalog.All.Length > 0 ? McpToolCatalog.All[0].Category : null;

            foreach (var entry in McpToolCatalog.All)
            {
                if (entry.Category != lastCategory)
                {
                    lastCategory = entry.Category;
                    EditorGUILayout.Space(entry.Category == firstCategory ? 0 : 10);
                    EditorGUILayout.LabelField(entry.Category, _categoryStyle);
                    EditorGUILayout.Space(4);
                }

                DrawToolCard(entry);
                EditorGUILayout.Space(6);
            }
        }

        private void DrawToolCard(McpToolCatalogEntry entry)
        {
            var enabled = McpProjectSettingsStore.IsToolEnabled(entry.Name);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Name", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(entry.Name, _nameStyle, GUILayout.Height(20));
                GUILayout.Space(8);
                EditorGUI.BeginChangeCheck();
                var next = GUILayout.Toggle(enabled, enabled ? "已启用" : "已关闭",
                    EditorStyles.miniButton, GUILayout.Width(48), GUILayout.Height(20));
                if (EditorGUI.EndChangeCheck())
                {
                    McpProjectSettingsStore.SetToolEnabled(entry.Name, next);
                    McpUnityBridge.PushToolPreferences();
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Description", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(entry.Description, _descStyle);
            EditorGUILayout.EndVertical();

            if (Event.current.type == EventType.Repaint)
            {
                var card = GUILayoutUtility.GetLastRect();
                var accent = enabled
                    ? new Color(0.32f, 0.75f, 0.48f, 1f)
                    : new Color(0.50f, 0.50f, 0.50f, 0.85f);
                EditorGUI.DrawRect(new Rect(card.x, card.y, 3f, card.height), accent);
            }
        }

        private void EnsureStyles()
        {
            if (_nameStyle == null)
            {
                _nameStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    wordWrap = false,
                    clipping = TextClipping.Clip,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(2, 2, 0, 0)
                };
            }

            if (_descStyle == null)
            {
                _descStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    fontSize = 11,
                    richText = false,
                    padding = new RectOffset(2, 2, 2, 4)
                };
                _descStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.78f, 0.78f, 0.78f)
                    : new Color(0.28f, 0.28f, 0.28f);
            }

            if (_categoryStyle == null)
            {
                _categoryStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft
                };
            }
        }
    }
}
