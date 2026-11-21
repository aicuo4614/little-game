using UnityEditor;

namespace KabuUnityMcp.Editor.Handlers.SystemTools
{
    /// <summary>
    /// 进 Play 时临时 DisableDomainReload，避免脚本 Domain Reload 拆掉 MCP WebSocket。
    /// 进 Play / 回到 Edit 后自动恢复用户原有 Enter Play Mode Options。
    /// </summary>
    internal static class EnterPlayModeOptionsGuard
    {
        private static bool savedEnabled;
        private static EnterPlayModeOptions savedOptions;
        private static bool active;
        private static bool subscribed;

        public static bool IsActive => active;

        public static void ApplyDisableDomainReload()
        {
            if (active)
                return;

            savedEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            savedOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            active = true;
            EnsureSubscribed();
        }

        public static void Restore()
        {
            if (!active)
                return;

            EditorSettings.enterPlayModeOptionsEnabled = savedEnabled;
            EditorSettings.enterPlayModeOptions = savedOptions;
            active = false;
        }

        private static void EnsureSubscribed()
        {
            if (subscribed)
                return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            subscribed = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode ||
                state == PlayModeStateChange.EnteredEditMode)
            {
                Restore();
            }
        }
    }
}
