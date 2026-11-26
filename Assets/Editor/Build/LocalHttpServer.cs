using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 在本地启动 Python HTTP 服务，将 CDN 根目录作为站点根目录。
/// 端口和路径均来自工程根目录 conf.json。支持 Windows / macOS / Linux。
/// </summary>
public static class LocalHttpServer
{
    private static Process serverProcess;

    [MenuItem("Kabu/启动本地 HTTP 服务", priority = 120)]
    public static void StartServer()
    {
        if (serverProcess != null && !serverProcess.HasExited)
        {
            EditorUtility.DisplayDialog(
                "提示",
                $"本地 HTTP 服务已在运行（PID {serverProcess.Id}）\n\n地址：http://localhost:{BuildConf.Port}",
                "确定");
            return;
        }

        string cdnRoot = BuildConf.CdnRootFolder;
        int port = BuildConf.Port;

        if (!Directory.Exists(cdnRoot))
        {
            Directory.CreateDirectory(cdnRoot);
            Debug.Log($"[LocalHttpServer] CDN 目录不存在，已自动创建: {cdnRoot}");
        }

        if (!TryResolvePython(out string pythonExe, out string pythonArgsPrefix, out string resolveError))
        {
            EditorUtility.DisplayDialog(
                "启动失败",
                $"未找到可用的 Python。\n\n{resolveError}\n\n请安装 Python 3，并确保命令行可执行 python 或 python3。",
                "确定");
            return;
        }

        string args = string.IsNullOrEmpty(pythonArgsPrefix)
            ? $"-m http.server {port}"
            : $"{pythonArgsPrefix} -m http.server {port}";

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = args,
            WorkingDirectory = cdnRoot,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true,
        };

        try
        {
            serverProcess = Process.Start(startInfo);
        }
        catch (Exception e)
        {
            Debug.LogError($"[LocalHttpServer] 启动失败: {e.Message}");
            EditorUtility.DisplayDialog("启动失败", $"无法启动 HTTP 服务：\n{e.Message}", "确定");
            serverProcess = null;
            return;
        }

        if (serverProcess == null || serverProcess.HasExited)
        {
            EditorUtility.DisplayDialog(
                "启动失败",
                $"Python 进程已退出。\n可执行文件：{pythonExe}\n参数：{args}\n目录：{cdnRoot}\n端口：{port}",
                "确定");
            serverProcess = null;
            return;
        }

        Debug.Log(
            $"[LocalHttpServer] 已启动 HTTP 服务\n  Python：{pythonExe} {args}\n  目录：{cdnRoot}\n  地址：http://localhost:{port}\n  PID：{serverProcess.Id}");
        EditorUtility.DisplayDialog(
            "本地 HTTP 服务已启动",
            $"地址：http://localhost:{port}\n目录：{cdnRoot}\nPID：{serverProcess.Id}\n\n关闭 Unity 或点击「停止」菜单可终止服务。",
            "确定");
    }

    [MenuItem("Kabu/停止本地 HTTP 服务", priority = 121)]
    public static void StopServer()
    {
        if (serverProcess == null || serverProcess.HasExited)
        {
            EditorUtility.DisplayDialog("提示", "本地 HTTP 服务未在运行。", "确定");
            return;
        }

        try
        {
            serverProcess.Kill();
            serverProcess.WaitForExit(3000);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LocalHttpServer] 停止时异常: {e.Message}");
        }

        serverProcess.Dispose();
        serverProcess = null;

        Debug.Log("[LocalHttpServer] HTTP 服务已停止");
        EditorUtility.DisplayDialog("提示", "本地 HTTP 服务已停止。", "确定");
    }

    [MenuItem("Kabu/停止本地 HTTP 服务", validate = true)]
    private static bool StopServerValidate()
    {
        return serverProcess != null && !serverProcess.HasExited;
    }

    /// <summary>
    /// 解析可用的 Python：Windows 优先 python，其次 py -3；Unix 优先 python3，其次 python。
    /// </summary>
    private static bool TryResolvePython(out string pythonExe, out string argsPrefix, out string error)
    {
        pythonExe = null;
        argsPrefix = null;
        error = null;

        if (Application.platform == RuntimePlatform.WindowsEditor)
        {
            if (CanRunPython("python", "--version"))
            {
                pythonExe = "python";
                return true;
            }

            if (CanRunPython("py", "-3 --version"))
            {
                pythonExe = "py";
                argsPrefix = "-3";
                return true;
            }

            error = "已尝试：python、py -3";
            return false;
        }

        if (CanRunPython("python3", "--version"))
        {
            pythonExe = "python3";
            return true;
        }

        if (CanRunPython("python", "--version"))
        {
            pythonExe = "python";
            return true;
        }

        error = "已尝试：python3、python";
        return false;
    }

    private static bool CanRunPython(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
            {
                if (p == null)
                {
                    return false;
                }

                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
        }
        catch
        {
            return false;
        }
    }
}
