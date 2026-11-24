using UnityEngine;

/// <summary>
/// 业务日志入口。API 对齐 <see cref="Debug"/>：<c>Log</c> / <c>LogWarning</c> / <c>LogError</c> / <c>LogException</c>。
/// 受 <see cref="Enabled"/> 控制（插件侧 <c>Debug.Log*</c> 不受影响）。
/// </summary>
public static class GameDebug
{
    /// <summary>
    /// 总开关。为 false 时屏蔽 Log/Warning/Error（Exception 仍输出）。
    /// 由 <see cref="LaunchSOConfig"/> 在启动时设置。
    /// </summary>
    public static bool Enabled = true;

    /// <summary>普通信息（对齐 <see cref="Debug.Log(object)"/>）。</summary>
    public static void Log(string tag, string msg)
    {
        if (!Enabled)
        {
            return;
        }

        Debug.Log(Format(tag, msg));
    }

    /// <summary>普通信息（带 Unity 上下文对象）。</summary>
    public static void Log(string tag, string msg, UnityEngine.Object context)
    {
        if (!Enabled)
        {
            return;
        }

        Debug.Log(Format(tag, msg), context);
    }

    /// <summary>警告（对齐 <see cref="Debug.LogWarning(object)"/>）。</summary>
    public static void LogWarning(string tag, string msg)
    {
        if (!Enabled)
        {
            return;
        }

        Debug.LogWarning(Format(tag, msg));
    }

    /// <summary>警告（带 Unity 上下文对象）。</summary>
    public static void LogWarning(string tag, string msg, UnityEngine.Object context)
    {
        if (!Enabled)
        {
            return;
        }

        Debug.LogWarning(Format(tag, msg), context);
    }

    /// <summary>错误（对齐 <see cref="Debug.LogError(object)"/>）。</summary>
    public static void LogError(string tag, string msg)
    {
        if (!Enabled)
        {
            return;
        }

        Debug.LogError(Format(tag, msg));
    }

    /// <summary>错误（带 Unity 上下文对象）。</summary>
    public static void LogError(string tag, string msg, UnityEngine.Object context)
    {
        if (!Enabled)
        {
            return;
        }

        Debug.LogError(Format(tag, msg), context);
    }

    /// <summary>异常（对齐 <see cref="Debug.LogException(System.Exception)"/>，始终输出）。</summary>
    public static void LogException(System.Exception ex)
    {
        Debug.LogException(ex);
    }

    private static string Format(string tag, string msg)
    {
        return $"[{tag}] {msg}";
    }
}
