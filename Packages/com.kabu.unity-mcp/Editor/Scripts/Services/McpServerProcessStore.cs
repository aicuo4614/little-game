using UnityEditor;

namespace KabuUnityMcp.Editor.Services
{
    /// <summary>
    /// Persists the launched server PID across domain reload.
    /// Must be read/written on the main thread (SessionState).
    /// </summary>
    internal static class McpServerProcessStore
    {
        private const string SessionPidKey = "KabuUnityMcp.ServerPid";

        public static int GetPid() => SessionState.GetInt(SessionPidKey, 0);

        public static void SetPid(int pid) => SessionState.SetInt(SessionPidKey, pid);

        public static void ClearPid() => SessionState.EraseInt(SessionPidKey);
    }
}
