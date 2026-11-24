using System;
using System.Diagnostics;
using System.Globalization;
using UnityEditor;

namespace KabuUnityMcp.Editor.Services
{
    // SessionState 跨域重载保留。核对启动时间，避免回收已被系统重用的 PID。
    internal static class McpServerProcessStore
    {
        private const string SessionPidKey = "KabuUnityMcp.ServerPid";
        private const string SessionStartKey = "KabuUnityMcp.ServerStartTicks";

        public static int GetPid()
        {
            var pid = SessionState.GetInt(SessionPidKey, 0);
            if (pid <= 0)
                return 0;

            try
            {
                using var process = Process.GetProcessById(pid);
                return StartTicks(process) == SessionState.GetString(SessionStartKey, "") ? pid : 0;
            }
            catch (ArgumentException)
            {
                return 0;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return 0;
            }
        }

        public static void SetProcess(Process process)
        {
            SessionState.SetString(SessionStartKey, StartTicks(process));
            SessionState.SetInt(SessionPidKey, process.Id);
        }

        public static void ClearPid()
        {
            SessionState.EraseInt(SessionPidKey);
            SessionState.EraseString(SessionStartKey);
        }

        private static string StartTicks(Process process) =>
            process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
    }
}
