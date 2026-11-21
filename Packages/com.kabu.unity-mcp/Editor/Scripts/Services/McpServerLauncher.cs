using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KabuUnityMcp.Editor.Models;
using KabuUnityMcp.Editor.Utils;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KabuUnityMcp.Editor.Services
{
    /// <summary>
    /// Starts / stops the local <c>dotnet run</c> process.
    /// 启动和停止只清理本工程的服务器进程。
    /// </summary>
    internal static class McpServerLauncher
    {
        private const int PortFreeWaitMs = 8000;
        private const int PortFreePollMs = 200;

        private const int ReadyWaitMs = 90000;
        private const int ReadyPollMs = 400;

        public static McpLaunchResult Start()
        {
            var host = McpServerEndpoints.Host;
            var port = McpServerEndpoints.Port;

            var csproj = PackagePaths.GetServerProjectPath();
            if (!File.Exists(csproj))
                return McpLaunchResult.Fail($"找不到 Server 工程：\n{csproj}");

            try
            {
                if (!EnsurePortAvailable(host, port, out var reclaimMsg))
                    return McpLaunchResult.Fail(reclaimMsg);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{csproj}\" --no-launch-profile",
                    WorkingDirectory = PackagePaths.GetServerDirectory(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                };
                startInfo.EnvironmentVariables["UNITY_MCP_ASPPORT"] = port.ToString();
                startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";
                startInfo.EnvironmentVariables["ASPNETCORE_URLS"] =
                    $"http://{host}:{port}";

                // Server owns its log file: Unity domain reload must not orphan redirected pipe readers.
                var logPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "KabuUnityMcp", "server.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                startInfo.EnvironmentVariables["UNITY_MCP_LOG_PATH"] = logPath;
                Debug.Log($"[kabu-unity-mcp] Server log: {logPath}");
                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                if (!process.Start())
                    return McpLaunchResult.Fail("无法启动 dotnet 进程。");



                McpServerProcessStore.SetProcess(process);
                McpServerStatusService.Instance.NotifyStarted(process.Id);
                Debug.Log($"[kabu-unity-mcp] Server starting (PID {process.Id}) → {McpServerEndpoints.McpUrl}");
                return McpLaunchResult.Success();
            }
            catch (Exception ex)
            {
                return McpLaunchResult.Fail(
                    $"{ex.Message}\n\n请确认本机已安装 .NET SDK，且 `dotnet` 在 PATH 中。");
            }
        }

        public static McpLaunchResult Stop()
        {
            var pid = McpServerProcessStore.GetPid();
            var serverDirectory = PackagePaths.GetServerDirectory();
            McpUnityBridge.PrepareForServerStop();
            _ = McpUnityBridge.DisconnectAsync();
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(200);
                    StopProjectProcesses(serverDirectory, pid);
                }
                finally
                {
                    EditorApplication.delayCall += () =>
                    {
                        McpServerProcessStore.ClearPid();
                        McpServerStatusService.Instance.NotifyStopped();
                        Debug.Log("[kabu-unity-mcp] Stop requested.");
                    };
                }
            });

            return McpLaunchResult.Success();
        }

        /// <summary>
        /// 端口已在听则直接成功；否则启动 Server 并等到 /health 或 TCP 就绪。
        /// </summary>
        public static async Task<McpLaunchResult> EnsureRunningAsync(int timeoutMs = ReadyWaitMs, CancellationToken cancellationToken = default)
        {
            var host = McpServerEndpoints.Host;
            var port = McpServerEndpoints.Port;

            // 已有进程在听：只等待就绪，绝不回收端口（避免误杀）
            if (McpPortProbe.IsPortOpen(host, port, 300))
            {
                if (await WaitUntilHealthyAsync(host, port, Math.Min(timeoutMs, 5000), cancellationToken))
                    return McpLaunchResult.Success();

                return McpLaunchResult.Fail("端口已占用，但 MCP 健康检查失败。请检查服务器日志。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var start = Start();
            if (!start.Ok)
                return start;

            if (await WaitUntilHealthyAsync(host, port, timeoutMs, cancellationToken))
                return McpLaunchResult.Success();

            return McpLaunchResult.Fail(
                $"服务器已启动，但在 {timeoutMs / 1000}s 内未就绪（{McpServerEndpoints.HealthUrl}）。\n" +
                "请查看 Console 里的 dotnet 输出，或稍后再点「连接」。");
        }

        /// <summary>轮询直到 HTTP /health 可用，支持取消。</summary>
        public static async Task<bool> WaitUntilHealthyAsync(
            string host, int port, int timeoutMs = ReadyWaitMs, CancellationToken cancellationToken = default)
        {
            var healthUrl = $"http://{host}:{port}/health";
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < timeoutMs)
            {
                if (McpPortProbe.IsPortOpen(host, port, 300))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (await ProbeHealthAsync(healthUrl).ConfigureAwait(false))
                        return true;
                }

                await Task.Delay(ReadyPollMs, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        private static async Task<bool> ProbeHealthAsync(string healthUrl)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(2)
                };
                using var response = await client.GetAsync(healthUrl).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static bool EnsurePortAvailable(string host, int port, out string error)
        {
            StopProjectProcesses(PackagePaths.GetServerDirectory(), McpServerProcessStore.GetPid());
            McpServerProcessStore.ClearPid();
            var timer = Stopwatch.StartNew();
            while (McpPortProbe.IsPortOpen(host, port))
            {
                if (timer.ElapsedMilliseconds >= PortFreeWaitMs)
                {
                    error = $"端口 {port} 仍被占用，请检查本工程独立端口的配置。";
                    return false;
                }

                Thread.Sleep(PortFreePollMs);
            }

            error = null;
            return true;
        }

        private static void StopProjectProcesses(string serverDirectory, int trackedPid)
        {
            if (trackedPid > 0)
                KillPidTree(trackedPid);

            // 编辑器重启后 SessionState 可能已丢失，仅回收从本工程目录启动的残留服务端。
            var prefix = Path.GetFullPath(serverDirectory).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            foreach (var process in Process.GetProcessesByName("kabu-unity-mcp"))
            {
                using (process)
                {
                    try
                    {
                        var executable = process.MainModule?.FileName;
                        if (executable != null && executable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            KillPidTree(process.Id);
                    }
                    catch (Exception ex)
                    {
                        MarshalLog($"检查 MCP 进程归属失败：{ex.Message}", isError: true);
                    }
                }
            }
        }

        private static void MarshalLog(string message, bool isError)
        {
            EditorApplication.delayCall += () =>
            {
                if (isError)
                    Debug.LogWarning($"[kabu-unity-mcp] {message}");
                else
                    Debug.Log($"[kabu-unity-mcp] {message}");
            };
        }

        private static bool KillPidTree(int pid)
        {
            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/PID {pid} /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(5000);
                    return true;
                }

                var process = Process.GetProcessById(pid);
                process.Kill();
                process.WaitForExit(3000);
                return true;
            }
            catch (Exception ex)
            {
                MarshalLog($"Kill PID {pid}: {ex.Message}", isError: true);
                return false;
            }
        }
    }
}
