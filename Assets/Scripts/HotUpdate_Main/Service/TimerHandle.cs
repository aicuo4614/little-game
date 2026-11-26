using System;
using UnityEngine;

/// <summary>
/// <see cref="TimerService.Delay"/> 返回的句柄；调用 <see cref="Cancel"/> 可中止尚未触发的回调。
/// </summary>
public sealed class TimerHandle
{
    TimerService owner;
    Coroutine coroutine;

    internal TimerHandle(TimerService owner)
    {
        this.owner = owner;
    }

    /// <summary>是否仍在等待触发（已触发或已取消则为 false）。</summary>
    public bool IsPending => coroutine != null;

    /// <summary>取消延时；已触发或已取消时无效果。</summary>
    public void Cancel()
    {
        if (owner == null || coroutine == null)
        {
            return;
        }

        owner.Cancel(this);
    }

    internal Coroutine Coroutine => coroutine;

    internal void Bind(Coroutine running)
    {
        coroutine = running;
    }

    internal void Clear()
    {
        coroutine = null;
        owner = null;
    }
}
