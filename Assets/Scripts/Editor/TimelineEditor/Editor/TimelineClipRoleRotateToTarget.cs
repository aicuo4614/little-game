using UnityEditor;
using UnityEngine;

/// <summary>单位朝向片段。绑定与采样通过组合组件处理，不直接持有施法者 Transform。</summary>
public sealed class TimelineClipRoleRotateToTarget : TimelineClipBase
{
    private TimelineClipRoleRotateToTargetConfig unitConfig;
    private TimelineUnitClipPreviewControl preview;

    /// <summary>初始化公共单位采样器，不改变单位姿态。</summary>
    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        unitConfig = (TimelineClipRoleRotateToTargetConfig)config;
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
        control.UnitBindings.DrawReference(unitConfig, ref unitConfig.Destination,
            ref unitConfig.DestinationBindingKey, ref unitConfig.ReferenceSelection, false);
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
        return false;
    }

    protected override void SetRect()
    {
        base.SetRect();
        rect.width = 60f;
    }

}
