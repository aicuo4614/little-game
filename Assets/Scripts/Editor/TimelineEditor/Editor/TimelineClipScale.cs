using UnityEditor;
using UnityEngine;

/// <summary>单位缩放片段。绑定与采样通过组合组件处理，不直接持有施法者 Transform。</summary>
public sealed class TimelineClipScale : TimelineClipBase
{
    private TimelineClipScaleConfig unitConfig;
    private TimelineUnitClipPreviewControl preview;

    /// <summary>初始化公共单位采样器，不改变单位姿态。</summary>
    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        unitConfig = (TimelineClipScaleConfig)config;
        base.Init(control, config);
        preview = new TimelineUnitClipPreviewControl(control, unitConfig, this);
        preview.Prepare();
    }

    public override void Draw()
    {
        preview.Prepare();
        base.Draw();
    }

    public override void DrawInspector()
    {
        base.DrawInspector();
        EditorGUI.BeginChangeCheck();
        control.UnitBindings.DrawSubject(unitConfig);
        unitConfig.DurationType = TimelineEnumDisplay.DrawDurationPopup(TimelineBindingEditorText.DurationMode, unitConfig.DurationType);
        unitConfig.DurationPraram = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            unitConfig.DurationType == DurationType.Time ? TimelineBindingEditorText.Duration : TimelineBindingEditorText.ScaleSpeed,
            unitConfig.DurationPraram));
        unitConfig.StartScale = EditorGUILayout.Vector3Field(TimelineBindingEditorText.StartScale, unitConfig.StartScale);
        unitConfig.EndScale = EditorGUILayout.Vector3Field(TimelineBindingEditorText.EndScale, unitConfig.EndScale);
        if (EditorGUI.EndChangeCheck())
        {
            preview.RequestRefresh();
        }
        EditorGUILayout.HelpBox(TimelineBindingEditorText.TransformHint, MessageType.Info);
        preview.DrawStatus();
    }

    public override void PreparePreviewSample()
    {
        preview.Prepare();
    }

    protected override void OnLeftIn()
    {
        base.OnLeftIn();
        preview.Capture();
    }

    protected override void OnInTimeLogic()
    {
        preview.Sample(playTiming);
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
        base.OnSetMyTransformPrefabPath(path);
        preview.Clear();
    }

    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        if (frameStep <= 0f || frameDelta == 0 || !string.IsNullOrEmpty(config.EndByOtherClipEnd))
        {
            return false;
        }
        preview.Prepare();
        var oldDuration = Mathf.Max(frameStep, config.Duration);
        var duration = SnapNudgeDuration(oldDuration, frameStep, frameDelta);
        unitConfig.DurationPraram = unitConfig.DurationType == DurationType.Time ? duration
            : unitConfig.DurationPraram * oldDuration / duration;
        preview.Prepare();
        preview.RequestRefresh();
        return true;
    }

}
