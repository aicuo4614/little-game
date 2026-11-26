using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>单位位移：独立选择移动角色与终点参考，按逻辑时间采样位置。</summary>
public sealed class TimelineClipTranslate : TimelineClipBase
{
    private TimelineClipTranslateConfig moveConfig;
    private TimelineUnitClipPreviewControl preview;
    private float rawDuration;

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        moveConfig = (TimelineClipTranslateConfig)config;
        moveConfig.PointData ??= new TimelineClipPointData
        {
            OffsetNodeType = UnitNodeType.Transform,
            OffsetScale = Vector3.one,
        };
        base.Init(control, config);
        preview = new TimelineUnitClipPreviewControl(control, moveConfig, this);
        SyncDuration();
    }

    public override void Draw()
    {
        SyncDuration();
        base.Draw();
    }

    public override void DrawInspector()
    {
        base.DrawInspector();
        EditorGUI.BeginChangeCheck();
        moveConfig.Subject = TimelineUnitBindingControl.DrawSimpleSubject(moveConfig.Subject);
        moveConfig.Destination = (TimelineMoveDestinationType)EditorGUILayout.Popup("终点", (int)moveConfig.Destination,
            new[] { TimelineBindingEditorText.CastTargets, TimelineBindingEditorText.EffectSource, TimelineBindingEditorText.EffectTargets,
                TimelineBindingEditorText.InitialPosition, TimelineBindingEditorText.WorldPosition });
        moveConfig.ReferenceSelection = TimelineUnitBindingControl.DrawReferenceSelection(
            moveConfig.Destination, moveConfig.ReferenceSelection);
        using (new EditorGUI.DisabledScope(moveConfig.Destination == TimelineMoveDestinationType.WorldPosition))
        {
            moveConfig.OffsetSpace = (TimelineMoveOffsetSpace)EditorGUILayout.Popup(TimelineBindingEditorText.OffsetSpace,
                (int)moveConfig.OffsetSpace, new[] { "接近方向", "挂点局部", "世界方向" });
        }
        moveConfig.DurationPraram = Mathf.Max(0.01f, EditorGUILayout.FloatField("时长（秒）", moveConfig.DurationPraram));
        moveConfig.IsRotateToAtStart = EditorGUILayout.Toggle(TimelineBindingEditorText.FaceDirection, moveConfig.IsRotateToAtStart);
        var references = GetReferences();
        if (NeedsReference())
        {
            moveConfig.PointData.OffsetNodeType = TimelineEnumDisplay.DrawNodePopup("终点挂点", moveConfig.PointData.OffsetNodeType);
        }
        var positionLabel = moveConfig.Destination == TimelineMoveDestinationType.WorldPosition
            ? TimelineBindingEditorText.WorldPosition
            : "终点偏移";
        moveConfig.PointData.OffsetPos = EditorGUILayout.Vector3Field(positionLabel, moveConfig.PointData.OffsetPos);
        if (EditorGUI.EndChangeCheck())
        {
            control.PreviewRefresh.Request();
        }
        if (moveConfig.OffsetSpace == TimelineMoveOffsetSpace.ApproachDirection
            && moveConfig.Destination != TimelineMoveDestinationType.WorldPosition)
        {
            TimelineEditorUtil.DrawStableHelp(TimelineBindingEditorText.ApproachOffsetHelp);
        }
        if (GetSubjects().Count == 0)
        {
            EditorGUILayout.HelpBox(TimelineBindingEditorText.MissingMovingUnit, MessageType.Warning);
        }
        else if (NeedsReference() && references.Count == 0)
        {
            EditorGUILayout.HelpBox(TimelineBindingEditorText.MissingDestination, MessageType.Warning);
        }
    }

    /// <summary>在重采样前按新参数重算时长。</summary>
    public override void PreparePreviewSample()
    {
        SyncDuration();
    }

    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        if (frameStep <= 0f || frameDelta == 0)
        {
            return false;
        }
        var duration = SnapNudgeDuration(Mathf.Max(frameStep, rawDuration), frameStep, frameDelta);
        if (moveConfig.DurationType == DurationType.Time)
        {
            moveConfig.DurationPraram = duration;
        }
        else
        {
            var distance = MeasureDistance();
            if (distance <= 0.0001f)
            {
                return false;
            }
            moveConfig.DurationPraram = distance / duration;
        }
        SyncDuration();
        return true;
    }

    protected override void OnLeftIn()
    {
        base.OnLeftIn();
        Capture();
    }

    protected override void OnInTimeLogic()
    {
        base.OnInTimeLogic();
        Sample();
    }

    protected override void OnRightOut()
    {
        base.OnRightOut();
        preview.Complete();
    }

    protected override void OnLeftOut()
    {
        base.OnLeftOut();
        preview.RestoreEntry();
    }

    public override void OnStopPreview()
    {
        preview.Clear();
        base.OnStopPreview();
    }

    public override void OnSetMyTransformPrefabPath(string path)
    {
        // Spine can create the preview caster before this clip's Init runs.
        preview?.Clear();
        base.OnSetMyTransformPrefabPath(path);
    }

    private List<UnitMono> GetSubjects()
    {
        return control.UnitBindings.ResolveUnits(config, moveConfig.Subject, moveConfig.SubjectBindingKey);
    }

    private List<UnitMono> GetReferences()
    {
        if (moveConfig.Destination == TimelineMoveDestinationType.CastTargets)
        {
            return control.PreviewBindings.GetTargets(config, false, moveConfig.DestinationBindingKey);
        }
        if (moveConfig.Destination == TimelineMoveDestinationType.EffectTargets)
        {
            return control.PreviewBindings.GetTargets(config, true, moveConfig.DestinationBindingKey);
        }
        var result = new List<UnitMono>();
        if (moveConfig.Destination == TimelineMoveDestinationType.EffectSource)
        {
            var source = control.PreviewBindings.GetSource(config, moveConfig.DestinationBindingKey);
            if (source)
            {
                result.Add(source);
            }
        }
        return result;
    }

    private bool NeedsReference()
    {
        return moveConfig.Destination != TimelineMoveDestinationType.InitialPosition
            && moveConfig.Destination != TimelineMoveDestinationType.WorldPosition;
    }

    private void Capture()
    {
        preview.Capture();
    }

    private float MeasureDistance()
    {
        return preview.MeasureRawDuration() * Mathf.Max(0.01f, moveConfig.DurationPraram);
    }

    private void SyncDuration()
    {
        preview.Prepare();
        rawDuration = preview.MeasureRawDuration();
    }

    private void Sample()
    {
        preview.Sample(playTiming);
    }

    public override void OnDrawGizmos()
    {
        preview.DrawEndpoints();
    }

    /// <summary>终点手柄按当前偏移空间反算配置，预览与实战复用同一坐标变换。</summary>
    public override void OnSceneGUI(SceneView sceneView)
    {
        base.OnSceneGUI(sceneView);
        preview.DrawMoveHandles(moveConfig);
    }
}
