using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using KabuUnityMcp.Editor.Models;
using KabuUnityMcp.Editor.Utils;
using UnityEditor;

namespace KabuUnityMcp.Editor.Services
{
    /// <summary>
    /// Background status monitor. UI only reads <see cref="Current"/>; probes never run in OnGUI.
    /// </summary>
    internal sealed class McpServerStatusService
    {
        public static McpServerStatusService Instance { get; } = new McpServerStatusService();

        private const double IntervalSeconds = 1.5;

        private readonly object _gate = new object();
        private McpServerStatusSnapshot _current = McpServerStatusSnapshot.Unknown;
        private int _retainCount;
        private bool _probeInFlight;
        private bool _refreshQueued;
        private double _nextProbeAt;
        private int _version;

        public McpServerStatusSnapshot Current
        {
            get
            {
                lock (_gate)
                    return _current;
            }
        }

        /// <summary>Raised on the main thread when the snapshot changes.</summary>
        public event Action Changed;

        public void Retain()
        {
            _retainCount++;
            if (_retainCount == 1)
            {
                EditorApplication.update += OnEditorUpdate;
                RequestRefresh();
            }
        }

        public void Release()
        {
            _retainCount = Math.Max(0, _retainCount - 1);
            if (_retainCount == 0)
                EditorApplication.update -= OnEditorUpdate;
        }

        public void RequestRefresh()
        {
            _nextProbeAt = 0;
            TryStartProbe();
        }

        /// <summary>启动成功后立刻刷新 UI，不必等下一轮探测。</summary>
        public void NotifyStarted(int pid)
        {
            ApplySnapshotDirect(new McpServerStatusSnapshot(McpServerRunState.RunningTracked, pid));
            RequestRefresh();
        }

        /// <summary>停止后立刻刷新 UI。</summary>
        public void NotifyStopped()
        {
            ApplySnapshotDirect(new McpServerStatusSnapshot(McpServerRunState.Stopped, 0));
            RequestRefresh();
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextProbeAt)
                return;
            TryStartProbe();
        }

        private void TryStartProbe()
        {
            if (_probeInFlight)
            {
                _refreshQueued = true;
                return;
            }

            _probeInFlight = true;
            _refreshQueued = false;
            _nextProbeAt = EditorApplication.timeSinceStartup + IntervalSeconds;

            // SessionState 必须在主线程读
            var trackedPid = McpServerProcessStore.GetPid();
            var host = McpServerEndpoints.Host;
            var port = McpServerEndpoints.Port;
            var version = Interlocked.Increment(ref _version);

            Task.Run(() =>
            {
                try
                {
                    var snapshot = BuildSnapshot(trackedPid, host, port);
                    EditorApplication.delayCall += () => ApplySnapshot(version, snapshot);
                }
                catch (Exception)
                {
                    EditorApplication.delayCall += () =>
                    {
                        _probeInFlight = false;
                        if (_refreshQueued)
                            TryStartProbe();
                    };
                }
            });
        }

        private void ApplySnapshot(int version, McpServerStatusSnapshot snapshot)
        {
            if (version != Volatile.Read(ref _version))
            {
                _probeInFlight = false;
                if (_refreshQueued)
                    TryStartProbe();
                return;
            }

            ApplySnapshotDirect(snapshot);
            _probeInFlight = false;

            // 仅当进程确已退出且端口也空时才清 PID，避免「启动中尚未 listen」的探测误清
            var storePid = McpServerProcessStore.GetPid();
            if (snapshot.State == McpServerRunState.Stopped &&
                storePid > 0 &&
                !IsProcessAlive(storePid) &&
                !McpPortProbe.IsPortOpen(McpServerEndpoints.Host, McpServerEndpoints.Port))
            {
                McpServerProcessStore.ClearPid();
            }

            if (_refreshQueued)
                TryStartProbe();
        }

        private void ApplySnapshotDirect(McpServerStatusSnapshot snapshot)
        {
            McpServerStatusSnapshot previous;
            lock (_gate)
            {
                previous = _current;
                _current = snapshot;
            }

            if (!SnapshotsEqual(previous, snapshot))
                Changed?.Invoke();
        }

        internal static McpServerStatusSnapshot BuildSnapshot(int trackedPid, string host, int port)
        {
            if (trackedPid > 0 && IsProcessAlive(trackedPid))
                return new McpServerStatusSnapshot(McpServerRunState.RunningTracked, trackedPid);

            if (McpPortProbe.IsPortOpen(host, port))
                return new McpServerStatusSnapshot(McpServerRunState.RunningExternal, 0);

            return new McpServerStatusSnapshot(McpServerRunState.Stopped, 0);
        }

        private static bool IsProcessAlive(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static bool SnapshotsEqual(McpServerStatusSnapshot a, McpServerStatusSnapshot b)
        {
            return a.State == b.State && a.Pid == b.Pid;
        }
    }
}
