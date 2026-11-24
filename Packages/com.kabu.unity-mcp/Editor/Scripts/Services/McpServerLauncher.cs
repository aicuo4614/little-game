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
    /// Start 前会回收占用端口/残留进程，避免 AddressInUse 与 exe 文件锁。
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



                McpServerProcessStore.SetPid(process.Id);
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
            var host = McpServerEndpoints.Host;
            var port = McpServerEndpoints.Port;
            McpUnityBridge.PrepareForServerStop();
            _ = McpUnityBridge.DisconnectAsync();
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(200);
                    ReclaimPort(host, port, pid);
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

        /// <summary>
        /// 若端口被占或存在残留 kabu-unity-mcp，先杀掉并等到端口空闲。
        /// </summary>
        private static bool EnsurePortAvailable(string host, int port, out string error)
        {
            error = null;
            var trackedPid = McpServerProcessStore.GetPid();
            var portBusy = McpPortProbe.IsPortOpen(host, port);
            var hasStray = HasServerProcess();

            if (!portBusy && trackedPid <= 0 && !hasStray)
                return true;

            Debug.Log(
                $"[kabu-unity-mcp] 启动前回收占用：portBusy={portBusy}, trackedPid={trackedPid}, strayServer={hasStray}");

            ReclaimPort(host, port, trackedPid);
            McpServerProcessStore.ClearPid();

            if (WaitUntilPortFree(host, port, PortFreeWaitMs))
                return true;

            error =
                $"端口 {port} 仍被占用，无法启动。\n\n" +
                $"请先点「关闭服务器」，或在任务管理器结束 kabu-unity-mcp / 占用 {port} 的进程，" +
                "也可在配置里换一个端口后再启动。";
            return false;
        }

        private static void ReclaimPort(string host, int port, int trackedPid)
        {
            if (trackedPid > 0)
                KillPidTree(trackedPid);

            KillServerProcessesByName();

            if (McpPortProbe.IsPortOpen(host, port))
                TryKillByPortWindows(port);
        }

        private static bool WaitUntilPortFree(string host, int port, int timeoutMs)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < timeoutMs)
            {
                if (!McpPortProbe.IsPortOpen(host, port))
                    return true;
                Thread.Sleep(PortFreePollMs);
            }

            return !McpPortProbe.IsPortOpen(host, port);
        }

        private static bool HasServerProcess()
        {
            try
            {
                foreach (var name in new[] { "kabu-unity-mcp", "kabu-unity-mcp.exe" })
                {
                    var list = Process.GetProcessesByName(
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            ? Path.GetFileNameWithoutExtension(name)
                            : name);
                    if (list != null && list.Length > 0)
                    {
                        foreach (var p in list)
                            p.Dispose();
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static void KillServerProcessesByName()
        {
            try
            {
                foreach (var process in Process.GetProcessesByName("kabu-unity-mcp"))
                {
                    try
                    {
                        var id = process.Id;
                        KillPidTree(id);
                        MarshalLog($"已结束残留进程 kabu-unity-mcp (PID {id})", isError: false);
                    }
                    catch (Exception ex)
                    {
                        MarshalLog($"结束 kabu-unity-mcp 失败：{ex.Message}", isError: true);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                MarshalLog($"查找 kabu-unity-mcp 失败：{ex.Message}", isError: true);
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

        private static bool TryKillByPortWindows(int port)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                return false;

            try
            {
                // Get-NetTCPConnection 在部分环境不可用；用 netstat 兜底
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments =
                        "-NoProfile -Command \"" +
                        "$pids = @(); " +
                        "try { $pids += @(Get-NetTCPConnection -LocalPort " + port +
                        " -State Listen -ErrorAction Stop | Select-Object -ExpandProperty OwningProcess) } catch {}; " +
                        "netstat -ano | ForEach-Object { if ($_ -match '[:.]" + port +
                        "\\s+.*LISTENING\\s+(\\d+)$') { $pids += [int]$Matches[1] } }; " +
                        "$pids | Select-Object -Unique | ForEach-Object { if ($_ -gt 0) { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue; Write-Output $_ } }" +
                        "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                };
                using var p = Process.Start(psi);
                if (p == null)
                    return false;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(8000);
                if (!string.IsNullOrWhiteSpace(output))
                    MarshalLog($"已按端口 {port} 结束进程：{output.Trim()}", isError: false);
                return true;
            }
            catch (Exception ex)
            {
                MarshalLog($"按端口结束进程失败：{ex.Message}", isError: true);
                return false;
            }
        }
    }
}
