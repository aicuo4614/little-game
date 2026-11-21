using KabuUnityMcp.Editor.Utils;

namespace KabuUnityMcp.Editor.Models
{
    /// <summary>
    /// Immutable status for UI. Built off the main thread; read anywhere.
    /// </summary>
    internal readonly struct McpServerStatusSnapshot
    {
        public McpServerRunState State { get; }
        public int Pid { get; }
        public bool IsRunning => State == McpServerRunState.RunningTracked
                                 || State == McpServerRunState.RunningExternal;

        public string StatusText
        {
            get
            {
                switch (State)
                {
                    case McpServerRunState.RunningTracked:
                        return $"Running (PID {Pid}) — {McpServerEndpoints.McpUrl}";
                    case McpServerRunState.RunningExternal:
                        return $"Port {McpServerEndpoints.Port} in use — {McpServerEndpoints.McpUrl}";
                    case McpServerRunState.Stopped:
                        return "Stopped";
                    default:
                        return "Checking…";
                }
            }
        }

        public McpServerStatusSnapshot(McpServerRunState state, int pid)
        {
            State = state;
            Pid = pid;
        }

        public static McpServerStatusSnapshot Unknown { get; } =
            new McpServerStatusSnapshot(McpServerRunState.Unknown, 0);
    }
}
