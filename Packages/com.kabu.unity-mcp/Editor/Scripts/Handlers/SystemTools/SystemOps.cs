using System;
using KabuUnityMcp.Editor.Handlers;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.SystemTools
{
    /// <summary>编辑器系统类工具（无 WS）。</summary>
    internal static class SystemOps
    {
        public static HandlerResult GetProjectInfo()
        {
            var scene = SceneManager.GetActiveScene();
            return HandlerResult.Success(new
            {
                productName = Application.productName,
                companyName = Application.companyName,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                dataPath = Application.dataPath,
                persistentDataPath = Application.persistentDataPath,
                isEditor = Application.isEditor,
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                activeScene = new
                {
                    name = scene.name,
                    path = scene.path,
                    isDirty = scene.isDirty,
                    rootCount = scene.rootCount
                },
                message = $"项目 {Application.productName} / Unity {Application.unityVersion}"
            });
        }

        public static HandlerResult GetPlayModeState()
        {
            string state;
            if (EditorApplication.isPlaying)
                state = EditorApplication.isPaused ? "Paused" : "Playing";
            else
                state = "Stopped";

            return HandlerResult.Success(new
            {
                state,
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                message = $"Play Mode：{state}"
            });
        }

        /// <summary>
        /// 同步请求进 Play（不等待）。异步等待请用 PlayModeTransitionHandlers。
        /// 单测 / 内部仍可用。
        /// </summary>
        public static HandlerResult EnterPlayMode(bool disableDomainReload = true)
        {
            if (EditorApplication.isPlaying)
                return HandlerResult.Fail("已处于 Play Mode。");

            if (disableDomainReload)
                EnterPlayModeOptionsGuard.ApplyDisableDomainReload();

            EditorApplication.isPlaying = true;
            return HandlerResult.Success(new
            {
                entered = true,
                disableDomainReload,
                message = "已请求进入 Play Mode（同步接口不等待；MCP 工具请走异步 Handler）。"
            });
        }

        /// <summary>同步请求退出 Play（不等待）。</summary>
        public static HandlerResult ExitPlayMode()
        {
            if (!EditorApplication.isPlaying)
                return HandlerResult.Success(new
                {
                    exited = false,
                    message = "已处于停止状态，无需退出。"
                });

            EditorApplication.isPlaying = false;
            return HandlerResult.Success(new
            {
                exited = true,
                message = "已请求退出 Play Mode（同步接口不等待）。"
            });
        }

        public static HandlerResult GetCompilationStatus()
        {
            var compiling = EditorApplication.isCompiling;
            var failed = EditorUtility.scriptCompilationFailed;
            return HandlerResult.Success(new
            {
                isCompiling = compiling,
                lastCompilationSucceeded = !failed,
                scriptCompilationFailed = failed,
                message = compiling
                    ? "脚本正在编译中。"
                    : failed
                        ? "最近一次编译失败。"
                        : "未在编译，最近编译成功。"
            });
        }

        public static HandlerResult RunMenuItem(string menuPath)
        {
            if (string.IsNullOrWhiteSpace(menuPath))
                return HandlerResult.Fail("请提供 menuPath，如 Edit/Undo 或 Assets/Refresh。", -32602);

            menuPath = menuPath.Trim();
            var ok = EditorApplication.ExecuteMenuItem(menuPath);
            if (!ok)
                return HandlerResult.Fail($"菜单项执行失败或不存在：{menuPath}");

            return HandlerResult.Success(new
            {
                menuPath,
                message = $"已执行菜单：{menuPath}"
            });
        }

        public static HandlerResult TriggerCompilation()
        {
            CompilationPipeline.RequestScriptCompilation();
            return HandlerResult.Success(new
            {
                requested = true,
                message = "已请求脚本重新编译（随后可能 Domain Reload）。"
            });
        }

        public static HandlerResult BringToForeground()
        {
            try
            {
#if UNITY_EDITOR_WIN
                // GetActiveWindow 在 Editor 后台时常为 0；优先用进程主窗口
                var hwnd = GetActiveWindow();
                if (hwnd == IntPtr.Zero)
                    hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, 9); // SW_RESTORE
                    SetForegroundWindow(hwnd);
                }
#endif
                // Cross-platform fallback: focus a known Unity window
                var wins = Resources.FindObjectsOfTypeAll<EditorWindow>();
                if (wins != null && wins.Length > 0 && wins[0] != null)
                    wins[0].Focus();

                return HandlerResult.Success(new { message = "已尝试将 Editor 置于前台。" });
            }
            catch (Exception ex)
            {
                return HandlerResult.Fail($"置于前台失败：{ex.Message}");
            }
        }

#if UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
#endif
    }
}
