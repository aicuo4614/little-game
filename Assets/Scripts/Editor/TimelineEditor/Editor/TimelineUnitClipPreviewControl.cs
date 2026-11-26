using UnityEditor;
using System.Collections.Generic;
using UnityEngine;

/// <summary>连接时间轴回调与共享单位采样器，集中处理参数刷新和编辑器定帧。</summary>
public sealed class TimelineUnitClipPreviewControl
{
    private TimelineControl control;
    private TimelineClipUnitConfigBase config;
    private TimelineUnitClipPlayback playback;
    private float rawDuration;
    private TimelineClipBase clip;
    private System.Func<BattleUnitTimelineActor, float> unitTime;

    /// <summary>创建片段专用的共用采样器。</summary>
    public TimelineUnitClipPreviewControl(TimelineControl control, TimelineClipUnitConfigBase config, TimelineClipBase clip)
    {
        this.control = control;
        this.config = config;
        this.clip = clip;
        unitTime = GetUnitTime;
        playback = new TimelineUnitClipPlayback(config, (subject, key) => control.UnitBindings.Resolve(config, subject, key));
    }

    /// <summary>重算导出时长，不修改单位姿态。</summary>
    public void Prepare()
    {
        rawDuration = playback.MeasureDuration();
        config.Duration = rawDuration;
        if (config is TimelineClipTranslateConfig move && move.AffectedByHitStop)
        {
            var units = control.UnitBindings.ResolveUnits(config, config.Subject, config.SubjectBindingKey);
            for (var i = 0; i < units.Count; i++)
            {
                config.Duration = Mathf.Max(config.Duration, TimelineEditorHitStopUtil.MeasureDisplayDurationForUnit(
                    control, units[i], clip.GetContentTimelineStartTime(), rawDuration));
            }
        }
    }

    /// <summary>采样片段内部时间，支持位移的逐单位定帧映射。</summary>
    public void Sample(float time)
    {
        playback.Sample(time, rawDuration,
            config is TimelineClipTranslateConfig move && move.AffectedByHitStop ? unitTime : null);
    }

    /// <summary>保存进入片段的单位状态，连续片段从上一段结果接入。</summary>
    public void Capture()
    {
        playback.Capture();
    }

    /// <summary>进入重建流程前清除片段快照。</summary>
    public void Clear()
    {
        playback.Clear();
    }

    /// <summary>参数改变后保留播放头，重建当前预览。</summary>
    public void RequestRefresh()
    {
        control.PreviewRefresh.Request();
    }

    /// <summary>缺少作用单位时提供一致提示。</summary>
    public void DrawStatus()
    {
        if (control.UnitBindings.ResolveUnits(config, config.Subject, config.SubjectBindingKey).Count == 0)
        {
            EditorGUILayout.HelpBox(TimelineBindingEditorText.MissingMovingUnit, MessageType.Warning);
        }
    }

    /// <summary>完成片段，精确落到终点。</summary>
    public void Complete()
    {
        playback.Sample(rawDuration, rawDuration);
    }

    /// <summary>反向离开时恢复片段进入状态。</summary>
    public void RestoreEntry()
    {
        playback.RestoreEntry();
    }

    /// <summary>返回不含定帧的片段时长。</summary>
    public float MeasureRawDuration()
    {
        return playback.MeasureDuration();
    }

    /// <summary>绘制位移片段已经捕获的终点。</summary>
    public void DrawEndpoints()
    {
        var states = PreviewStates();
        for (var i = 0; i < states.Count; i++)
        {
            EditorTimelineUtil.DrawCirclePoint(Color.red, states[i].End, 0.1f);
        }
    }

    /// <summary>在 shared 捕获的坐标系上编辑终点。</summary>
    public void DrawMoveHandles(TimelineClipTranslateConfig move)
    {
        var states = PreviewStates();
        for (var i = 0; i < states.Count; i++)
        {
            var state = states[i];
            var rotation = Quaternion.identity;
            if (move.Destination != TimelineMoveDestinationType.WorldPosition)
            {
                if (move.OffsetSpace == TimelineMoveOffsetSpace.NodeLocal)
                {
                    rotation = state.AnchorRotation;
                }
                else if (move.OffsetSpace == TimelineMoveOffsetSpace.ApproachDirection)
                {
                    TimelineMoveOffsetUtil.Basis(state.Position, state.Anchor, state.OffsetDirection, out _, out var side);
                    rotation = Quaternion.LookRotation(side, Vector3.up);
                }
            }
            Handles.color = Color.cyan;
            Handles.DrawDottedLine(state.Position, state.End, 4f);
            EditorGUI.BeginChangeCheck();
            var next = Handles.PositionHandle(state.End, rotation);
            if (EditorGUI.EndChangeCheck())
            {
                move.PointData.OffsetPos = move.Destination == TimelineMoveDestinationType.WorldPosition ? next
                    : TimelineMoveOffsetUtil.FromWorld(state.Position, state.Anchor, next, move.OffsetSpace,
                        state.AnchorRotation, state.AnchorScale, state.OffsetDirection);
                playback.Clear();
                control.PreviewRefresh.Request();
                break;
            }
        }
    }

    private IReadOnlyList<TimelineUnitClipState> PreviewStates()
    {
        var states = control.SharedPlayback.GetUnitStates(config.ClipKey);
        if (states.Count > 0)
        {
            return states;
        }
        playback.Capture();
        return playback.States;
    }

    private float GetUnitTime(BattleUnitTimelineActor actor)
    {
        return TimelineEditorHitStopUtil.GetClipLogicPlayTiming(control, control.UnitBindings.FindUnit(actor),
            clip.GetContentTimelineStartTime(), (float)control.Timeline.RunningTime);
    }
}
