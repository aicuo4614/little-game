using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 延时 / 重复回调（协程宿主）。通过 <c>MVC.TimerService</c> 获取。
/// </summary>
public sealed class TimerService : MonoBehaviour, IService
{
    /// <summary>创建 DontDestroyOnLoad 宿主并返回组件。</summary>
    public static TimerService CreateHost()
    {
        var go = new GameObject("TimerService");
        UnityEngine.Object.DontDestroyOnLoad(go);
        return go.AddComponent<TimerService>();
    }

    /// <summary>延迟调用；返回句柄，可 <see cref="TimerHandle.Cancel"/>。</summary>
    public TimerHandle Delay(float seconds, Action callback)
    {
        var handle = new TimerHandle(this);
        handle.Bind(StartCoroutine(DelayCoroutine(seconds, callback, handle)));
        return handle;
    }

    /// <summary>延迟重复调用；<paramref name="repeatCount"/> 为 -1 表示无限。</summary>
    public void DelayRepeat(float seconds, Action callback, int repeatCount = -1)
    {
        StartCoroutine(RepeatCoroutine(seconds, callback, repeatCount));
    }

    /// <summary>取消尚未触发的延时。</summary>
    internal void Cancel(TimerHandle handle)
    {
        if (handle == null || handle.Coroutine == null)
        {
            return;
        }

        StopCoroutine(handle.Coroutine);
        handle.Clear();
    }

    IEnumerator DelayCoroutine(float seconds, Action callback, TimerHandle handle)
    {
        if (seconds > 0f)
        {
            yield return new WaitForSeconds(seconds);
        }

        handle.Clear();
        callback?.Invoke();
    }

    IEnumerator RepeatCoroutine(float seconds, Action callback, int count)
    {
        var executed = 0;
        while (count < 0 || executed < count)
        {
            yield return new WaitForSeconds(seconds);
            callback?.Invoke();
            executed++;
        }
    }
}
