using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.UI
{
    /// <summary>Shared IMGUI helpers for MCP Editor windows.</summary>
    internal static class McpEditorGui
    {
        public static void CopyRow(string label, string value, float labelWidth = 90f)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
                EditorGUILayout.SelectableLabel(value, EditorStyles.textField, GUILayout.Height(18));
                if (GUILayout.Button("复制", GUILayout.Width(48)))
                    CopyToClipboard(label, value);
            }
        }

        public static void CopyableJson(string title, string json, ref GUIStyle jsonStyle)
        {
            CopyableSnippet(title, json, "复制 JSON", ref jsonStyle);
        }

        public static void CopyableSnippet(string title, string text, string buttonLabel, ref GUIStyle textStyle)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (textStyle == null)
            {
                textStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true,
                    richText = false
                };
            }

            var height = Mathf.Clamp(
                textStyle.CalcHeight(new GUIContent(text), EditorGUIUtility.currentViewWidth - 40f),
                72f, 160f);
            EditorGUILayout.SelectableLabel(text, textStyle, GUILayout.Height(height));
            if (GUILayout.Button(buttonLabel, GUILayout.Height(24)))
                CopyToClipboard(title, text);
        }

        public static void CopyToClipboard(string label, string value)
        {
            EditorGUIUtility.systemCopyBuffer = value;
            Debug.Log($"[kabu-unity-mcp] 已复制 {label}: {value}");
        }
    }
}
