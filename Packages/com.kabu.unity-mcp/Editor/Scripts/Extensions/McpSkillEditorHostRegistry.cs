using KabuUnityMcp.Editor.Services;
using UnityEngine;

namespace KabuUnityMcp.Editor.Extensions
{
    /// <summary>工程侧注册/注销技能编辑器 Host；未注册时 SkillEditor 工具应失败或从 enabledTools 隐藏。</summary>
    public static class McpSkillEditorHostRegistry
    {
        public static IMcpSkillEditorHost Current { get; private set; }

        public static bool HasHost => Current != null;

        public static void Register(IMcpSkillEditorHost host)
        {
            if (host == null)
            {
                throw new System.ArgumentNullException(nameof(host));
            }

            Current = host;
            Debug.Log("[kabu-unity-mcp] SkillEditor Host registered.");
            McpUnityBridge.PushToolPreferences();
        }

        public static void Unregister(IMcpSkillEditorHost host)
        {
            if (Current != host)
            {
                return;
            }

            Current = null;
            Debug.Log("[kabu-unity-mcp] SkillEditor Host unregistered.");
            McpUnityBridge.PushToolPreferences();
        }
    }
}
