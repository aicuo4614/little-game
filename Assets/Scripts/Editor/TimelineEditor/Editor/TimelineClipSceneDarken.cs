using UnityEditor;
using UnityEngine;

public class TimelineClipSceneDarken : TimelineClipBase
{
    private TimelineClipSceneDarkenConfig thisConfig;
    private Color savedMultiplyColor;
    private bool hasSavedMultiplyColor;

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipSceneDarkenConfig;
        base.Init(control, config);
    }

    public override void DrawInspector()
    {
        base.DrawInspector();

        EditorGUILayout.LabelField("Scene Darken", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        thisConfig.StartStrength = EditorGUILayout.Slider("起始强度", thisConfig.StartStrength, 0f, 1f);
        thisConfig.EndStrength = EditorGUILayout.Slider("结束强度", thisConfig.EndStrength, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            control.Timeline.Repaint();
        }
    }

    protected override void OnInTime()
    {
        base.OnInTime();
        hasSavedMultiplyColor = SceneDarkenUtil.TryGetMultiplyColor(out savedMultiplyColor);
    }

    /// <summary>离开 Clip 后不自动清压暗；向右出保持 EndStrength，向左出才恢复入 Clip 前状态。</summary>
    protected override void OnRightOut()
    {
        base.OnRightOut();
        ApplyDarken(config.Duration);
    }

    protected override void OnLeftOut()
    {
        base.OnLeftOut();
        RestoreMultiplyColor();
    }

    protected override void OnInTimeLogic()
    {
        base.OnInTimeLogic();
        ApplyDarken(playTiming);
    }

    public override void OnStopPreview()
    {
        RestoreMultiplyColor();
        base.OnStopPreview();
    }

    public override void OnDestroy()
    {
        RestoreMultiplyColor();
        base.OnDestroy();
    }

    private void RestoreMultiplyColor()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            SceneDarkenUtil.ClearEditorPreview();
            hasSavedMultiplyColor = false;
            return;
        }
#endif

        if (hasSavedMultiplyColor)
        {
            SceneDarkenUtil.SetMultiplyColor(savedMultiplyColor);
        }

        hasSavedMultiplyColor = false;
    }

    private void ApplyDarken(float localTime)
    {
        if (thisConfig == null)
        {
            return;
        }
        var strength = TimelineClipSceneDarkenUtil.EvaluateStrength(thisConfig, localTime, config.Duration);
        SceneDarkenUtil.SetMultiplyColor(SceneDarkenUtil.ResolveMultiplyColor(strength));
    }
}
