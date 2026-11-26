using System;
using UnityEditor;

/// <summary>参数变化后保留播放头，在片段边界重建当前预览画面。</summary>
public sealed class TimelinePreviewRefreshControl
{
    private TimelineControl control;
    private TimelinePreviewRefreshStateType state;
    private double requestedTime;

    /// <summary>绑定时间轴控制器。</summary>
    public TimelinePreviewRefreshControl(TimelineControl control)
    {
        this.control = control;
    }

    /// <summary>暂停时间推进，将本轮 Inspector 修改合并为一次重采样。</summary>
    public void Request()
    {
        if (state == TimelinePreviewRefreshStateType.Sampling)
        {
            return;
        }
        requestedTime = control.Timeline.RunningTime;
        control.Timeline.PausePreView();
        control.Timeline.SeekRunningTimeImmediate(requestedTime);
        ChangeState(TimelinePreviewRefreshStateType.Pending);
    }

    /// <summary>显式停止、加载或销毁时取消尚未执行的刷新。</summary>
    public void Cancel()
    {
        if (state != TimelinePreviewRefreshStateType.Sampling)
        {
            ChangeState(TimelinePreviewRefreshStateType.Idle);
        }
    }

    /// <summary>在下一次画布绘制前统一处理，采样过程不绘制 UI。</summary>
    public void Process()
    {
        if (state != TimelinePreviewRefreshStateType.Pending)
        {
            return;
        }
        ChangeState(TimelinePreviewRefreshStateType.Sampling);
        var window = control.Timeline;
        try
        {
            control.OnStopPreview();
            foreach (var clip in control.Clips)
            {
                clip.ResetPreviewSample();
            }
            window.SeekRunningTimeImmediate(requestedTime);
            control.UnitsControl.Draw();
            control.SharedPlayback.Evaluate();
        }
        finally
        {
            window.SeekRunningTimeImmediate(requestedTime);
            ChangeState(TimelinePreviewRefreshStateType.Idle);
            window.Repaint();
            SceneView.RepaintAll();
        }
    }

    private void ChangeState(TimelinePreviewRefreshStateType next)
    {
        if (state == next)
        {
            return;
        }
        state = next;
    }
}
