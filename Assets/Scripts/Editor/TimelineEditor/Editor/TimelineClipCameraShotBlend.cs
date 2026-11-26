using DG.Tweening;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 运镜过渡（编辑器）：上一条运镜模板终点（或默认机位）→ 下一条运镜模板起点。
/// </summary>
public class TimelineClipCameraShotBlend : TimelineClipBase
{
    private TimelineClipCameraShotBlendConfig thisConfig;
    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipCameraShotBlendConfig;
        base.Init(control, config);
    }

    public override void DrawInspector()
    {
        base.DrawInspector();

        EditorGUILayout.LabelField("运镜过渡", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "从「上一条运镜模板终点」（没有则用默认战斗机位）插值到下一条运镜的模板起点。\n"
                + "不抓实时机位，正 scrub / 倒 scrub 同一时刻画面一致。\n"
                + "可 Catch 指定目标运镜；不填则按时间自动找下一条。",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(true);
        thisConfig.NextCameraShotClipKey = EditorGUILayout.TextField(
            "目标运镜ClipKey",
            thisConfig.NextCameraShotClipKey ?? string.Empty);
        EditorGUI.EndDisabledGroup();
        if (GUILayout.Button("Catch", GUILayout.Width(64)))
        {
            control.BeginCameraShotBlendCatch(this);
        }

        if (GUILayout.Button("清除", GUILayout.Width(48)))
        {
            thisConfig.NextCameraShotClipKey = "";
        }

        EditorGUILayout.EndHorizontal();

        if (control.IsWaitingCameraShotBlendCatch(this))
        {
            EditorGUILayout.HelpBox("Catch 模式：请在时间轴点击选择目标「相机运镜」Clip", MessageType.Warning);
        }

        var resolved = ResolveNextShotConfig();
        if (resolved != null)
        {
            EditorGUILayout.HelpBox(
                "将过渡到：" + UnitFeatureTypeDisplay.GetClipLabelName(resolved)
                    + "（" + TimelineEnumDisplay.GetDisplayName(resolved.ShotType) + "）",
                MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox("未找到目标运镜：请 Catch 一条 CameraShot，或在本条之后放置运镜。", MessageType.Warning);
        }

        thisConfig.BlendEase = (Ease)EditorGUILayout.EnumPopup("过渡缓动", thisConfig.BlendEase);
        thisConfig.BlendFov = EditorGUILayout.Toggle("过渡FOV", thisConfig.BlendFov);

        if (EditorGUI.EndChangeCheck())
        {
            control.SharedPlayback.Invalidate();
            control.Timeline.Repaint();
            SceneView.RepaintAll();
        }
    }

    private TimelineClipCameraShotConfig ResolveNextShotConfig()
    {
        if (thisConfig == null || control?.Config?.Clips == null)
        {
            return null;
        }

        var contentStart = GetContentTimelineStartTime();
        var contentEnd = control.ResolveClipContentEndTime(config);
        return TimelineClipCameraShotBlendUtil.ResolveNextCameraShot(
            control.Config.Clips,
            thisConfig.NextCameraShotClipKey,
            config.ClipKey,
            contentStart,
            contentEnd,
            shot => control.Clips.Find(item => item.Config == shot).GetContentTimelineStartTime());
    }

}
