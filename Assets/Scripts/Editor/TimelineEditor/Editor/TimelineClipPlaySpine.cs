using UnityEditor;
using UnityEngine;

/// <summary>
/// 旧版 PlaySpine 兼容占位（本项目使用 3D Animator，请优先使用 PlayAnimator）。
/// 加载历史 JSON 时自动回退到 3D Animator 采样，避免空引用或报错。
/// </summary>
public sealed class TimelineClipPlaySpine : TimelineClipBase
{
    private TimelineClipPlaySpineConfig spineConfig;

    public override void PreInit(TimelineControl control, TimelineClipConfigBase config)
    {
        spineConfig = (TimelineClipPlaySpineConfig)config;
        prefabPath = string.IsNullOrEmpty(control.MyTransformPath)
            ? EditorTimelineAssetPathUtil.DefaultUnitLogicalName
            : control.MyTransformPath;
        isBaseTransform = true;
        base.Init(control, config);
        RefreshDuration();
    }

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
    }

    public override void DrawInspector()
    {
        base.DrawInspector();
        EditorGUILayout.HelpBox("本项目使用 3D Animator，PlaySpine 仅作历史兼容占位，请改用「单位 播放动画 (PlayAnimator)」。", MessageType.Warning);
        if (spineConfig != null)
        {
            spineConfig.AnimationName = EditorGUILayout.TextField("兼容动画名", spineConfig.AnimationName ?? string.Empty);
            spineConfig.Speed = Mathf.Max(0.01f, EditorGUILayout.FloatField("速度", spineConfig.Speed));
        }
    }

    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta) => false;

    private void RefreshDuration()
    {
        if (instance == null || spineConfig == null)
        {
            return;
        }

        var animator = instance.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            var len = EditorAnimatorStateUtil.GetStateLength(animator.runtimeAnimatorController, spineConfig.AnimationName);
            if (len > 0f)
            {
                config.Duration = len / Mathf.Max(0.01f, spineConfig.Speed);
            }
        }
    }
}
