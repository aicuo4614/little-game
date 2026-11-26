using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class TimelineClipPlayHit : TimelineClipBase
{
    private TimelineClipPlayHitConfig thisConfig;
    private List<Animator> enemyAnimators = new List<Animator>();
    private List<int> enemyStateHashes = new List<int>();
    private List<GameObject> effectInstances = new List<GameObject>();
    private List<List<ParticleSystem>> effectParticleRoots = new List<List<ParticleSystem>>();
    private float animationLength;
    /// <summary>受击逻辑内容时长（动画或手调）；展示 Duration = 逻辑时长 + 与定帧交叉重叠。</summary>
    private float rawHitContentDuration;
    private bool rawHitContentInitialized;
    private float lastWrittenDisplayDuration = -1f;

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipPlayHitConfig;
        if (!string.IsNullOrEmpty(thisConfig.EffectPath))
        {
            thisConfig.EffectPath = EditorTimelineAssetPathUtil.ToEffectLogicalName(thisConfig.EffectPath);
            prefabPath = thisConfig.EffectPath;
        }

        base.Init(control, config);
        RefreshAnimationLength();
    }

    public override void Draw()
    {
        SyncHitDisplayDuration();
        base.Draw();
        UpdateEffectTransforms();
    }

    /// <summary>快捷键改受击逻辑时长；纯动画自动模式不可改。</summary>
    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        if (config == null || frameDelta == 0 || frameStep <= 0f)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(config.EndByOtherClipEnd) || ShouldAutoDurationFromAnimation())
        {
            return false;
        }

        SyncHitDisplayDuration();
        var minDuration = Mathf.Max(frameStep, animationLength);
        rawHitContentDuration = Mathf.Max(minDuration, SnapNudgeDuration(rawHitContentDuration, frameStep, frameDelta));
        rawHitContentInitialized = true;
        SyncHitDisplayDuration();
        return true;
    }

    public override void DrawInspector()
    {
        base.DrawInspector();
        control.PreviewBindings.DrawBinding(config, true);

        // base「时长」若被改成与当前展示值不同，视为在改逻辑时长（仅非纯动画自动模式）
        if (!ShouldAutoDurationFromAnimation())
        {
            if (thisConfig.AffectedByHitStop)
            {
                var expectedDisplay = TimelineEditorHitStopUtil.MeasureDisplayDurationForAnyHitStop(
                    control,
                    GetContentTimelineStartTime(),
                    rawHitContentDuration);
                if (!Mathf.Approximately(config.Duration, expectedDisplay)
                    && !Mathf.Approximately(config.Duration, lastWrittenDisplayDuration))
                {
                    rawHitContentDuration = Mathf.Max(config.Duration, animationLength, 0.01f);
                    rawHitContentInitialized = true;
                }
            }
            else if (!Mathf.Approximately(config.Duration, lastWrittenDisplayDuration))
            {
                rawHitContentDuration = Mathf.Max(config.Duration, animationLength, 0.01f);
                rawHitContentInitialized = true;
            }
        }

        EditorGUILayout.LabelField("Play Hit", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            thisConfig.AffectedByHitStop
                ? "条长 = 受击逻辑时长 + 与「角色定帧」交叉重叠；定帧变短时条长会跟着收回。\n受击动画与受击特效均按定帧逻辑时间采样。"
                : "未勾选「受定帧影响」：条长 = 受击逻辑时长；受击动画与特效均跟全局时间轴。",
            MessageType.Info);
        thisConfig.AffectedByHitStop = DrawAffectedByHitStopToggle(thisConfig.AffectedByHitStop);
        DrawAnimatorParamField();
        EditorGUI.BeginChangeCheck();
        thisConfig.AnimatorDelayTime = Mathf.Max(0f, EditorGUILayout.FloatField("受击动画延迟", thisConfig.AnimatorDelayTime));
        if (EditorGUI.EndChangeCheck())
        {
            control.Timeline.StopPreView();
            RefreshAnimationLength();
        }

        SyncHitDisplayDuration();

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.FloatField("动画时长", animationLength);
        EditorGUILayout.FloatField("受击逻辑时长", rawHitContentDuration);
        if (thisConfig.AffectedByHitStop)
        {
            var overlap = TimelineEditorHitStopUtil.GetOverlapWithAnyHitStop(
                control,
                GetContentTimelineStartTime(),
                rawHitContentDuration);
            EditorGUILayout.FloatField("与定帧交叉重叠", overlap);
        }

        EditorGUILayout.FloatField("展示条长", config.Duration);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(4);
        EditorGUI.BeginChangeCheck();
        var nextEffectPath = EditorGUILayout.TextField("逻辑名", thisConfig.EffectPath ?? string.Empty);
        if (EditorGUI.EndChangeCheck())
        {
            thisConfig.EffectPath = EditorTimelineAssetPathUtil.ToEffectLogicalName(nextEffectPath);
            prefabPath = thisConfig.EffectPath;
            prefab = string.IsNullOrEmpty(prefabPath)
                ? null
                : EditorTimelineAssetPathUtil.LoadEffectPrefab(prefabPath);
            RefreshAnimationLength();
        }

        var prefabObj = EditorTimelineAssetPathUtil.LoadEffectPrefab(thisConfig.EffectPath);
        EditorGUI.BeginChangeCheck();
        var newPrefab = (GameObject)EditorGUILayout.ObjectField("特效 Prefab", prefabObj, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            if (newPrefab == null)
            {
                ApplyEffectPath(string.Empty);
            }
            else
            {
                ApplyEffectPath(EditorTimelineAssetPathUtil.EffectLogicalFromAssetPath(
                    AssetDatabase.GetAssetPath(newPrefab)));
            }

            control.Timeline.StopPreView();
        }

        TimelineEditorUtil.PointDataInspector(
            this.control,
            "特效挂点",
            thisConfig.UnitNodePointData,
            out bool isBigChange,
            out bool isSmallChange,
            showUseOriginalOptions: true,
            showDontFollowOption: true,
            nodeHost: TimelineEditorUtil.ResolveHitEffectNodeHost(this.control, config));
        if (isBigChange)
        {
            control.Timeline.StopPreView();
        }
        else if (isSmallChange)
        {
            ForceApplyEffectTransforms();
            SceneView.RepaintAll();
            control.Timeline.Repaint();
        }
    }

    public override void OnDestroy()
    {
        ClearEffectInstances();
        base.OnDestroy();
    }

    protected override void OnOutTime()
    {
        ClearEffectInstances();
        enemyAnimators.Clear();
        enemyStateHashes.Clear();
        base.OnOutTime();
    }

    protected override void OnInTime()
    {
        if (prefab == null && !string.IsNullOrEmpty(prefabPath))
        {
            prefab = EditorTimelineAssetPathUtil.LoadEffectPrefab(prefabPath);
        }

        CacheEnemyAnimators();
        CreateEffectInstancesOnEnemies();
        RefreshAnimationLength();
    }

    protected override void OnInTimeLogic()
    {
        base.OnInTimeLogic();
        PlayHitAnimationOnEnemies();
        SimulateParticles();
    }

    private void DrawAnimatorParamField()
    {
        var clipNames = GetEnemyAnimatorClipNames();
        var options = new List<string> { "(无)" };
        options.AddRange(clipNames);

        var currentIndex = 0;
        var paramName = thisConfig.AnimatorParamName ?? string.Empty;
        for (var i = 0; i < clipNames.Count; i++)
        {
            if (clipNames[i] == paramName)
            {
                currentIndex = i + 1;
                break;
            }
        }

        EditorGUI.BeginChangeCheck();
        var newIndex = EditorGUILayout.Popup("受击动画名", currentIndex, options.ToArray());
        if (EditorGUI.EndChangeCheck())
        {
            var name = newIndex == 0 ? string.Empty : clipNames[newIndex - 1];
            control.Timeline.StopPreView();
            thisConfig.AnimatorParamName = name;
            thisConfig.SpineAnimationName = string.Empty;
            OnAnimatorParamChanged();
        }
    }

    private void OnAnimatorParamChanged()
    {
        RefreshAnimationLength();
        control.Timeline.Repaint();
    }

    private void ApplyEffectPath(string newPath)
    {
        var logical = string.IsNullOrEmpty(newPath)
            ? string.Empty
            : EditorTimelineAssetPathUtil.ToEffectLogicalName(newPath);
        thisConfig.EffectPath = logical;
        prefabPath = logical;
        prefab = string.IsNullOrEmpty(logical)
            ? null
            : EditorTimelineAssetPathUtil.LoadEffectPrefab(logical);
        ClearEffectInstances();
        ChangeClipTimeStateType(ClipTimeStateType.OutTime);
        RefreshAnimationLength();
    }

    public override void OnPreviewQualityChanged()
    {
        base.OnPreviewQualityChanged();
        ClearEffectInstances();
        RefreshAnimationLength();
    }

    private bool ShouldAutoDurationFromAnimation()
    {
        return (!string.IsNullOrEmpty(thisConfig.AnimatorParamName)
            || !string.IsNullOrEmpty(thisConfig.SpineAnimationName))
            && string.IsNullOrEmpty(thisConfig.EffectPath);
    }

    private void CacheEnemyAnimators()
    {
        enemyAnimators.Clear();
        enemyStateHashes.Clear();

        if (string.IsNullOrEmpty(thisConfig.AnimatorParamName))
        {
            return;
        }

        var targets = control.PreviewBindings.GetTargets(config, true);
        var stateHash = Animator.StringToHash(thisConfig.AnimatorParamName);

        for (var i = 0; i < targets.Count; i++)
        {
            enemyAnimators.Add(targets[i].Animator);
            enemyStateHashes.Add(stateHash);
        }
    }

    private void PlayHitAnimationOnEnemies()
    {
        if (string.IsNullOrEmpty(thisConfig.AnimatorParamName) || animationLength <= 0f)
        {
            return;
        }

        var contentStart = GetContentTimelineStartTime();
        var runningTime = (float)control.Timeline.RunningTime;
        var logicTiming = thisConfig.AffectedByHitStop
            ? TimelineEditorHitStopUtil.GetClipLogicPlayTimingForAnyHitStopClippedToRaw(
                control,
                contentStart,
                rawHitContentDuration,
                runningTime)
            : playTiming;
        var animTime = logicTiming - thisConfig.AnimatorDelayTime;
        if (animTime < 0f)
        {
            animTime = 0f;
        }

        var normalizedTime = animTime / animationLength;
        for (var i = 0; i < enemyAnimators.Count; i++)
        {
            var animator = enemyAnimators[i];
            if (animator == null || !animator.isActiveAndEnabled)
            {
                continue;
            }

            animator.speed = 0f;
            animator.Play(enemyStateHashes[i], 0, normalizedTime);
            animator.Update(0f);
        }
    }

    private void CreateEffectInstancesOnEnemies()
    {
        ClearEffectInstances();

        if (string.IsNullOrEmpty(thisConfig.EffectPath) || prefab == null || thisConfig.UnitNodePointData == null)
        {
            return;
        }

        var targets = control.PreviewBindings.GetTargets(config, true);
        for (var i = 0; i < targets.Count; i++)
        {
            var effectGo = Object.Instantiate(prefab);
            TimelineClipPlayHitUtil.ApplyEffectTransform(
                effectGo.transform,
                thisConfig.UnitNodePointData,
                targets[i],
                GetEffectSourceOriginPos());
            effectInstances.Add(effectGo);

            List<ParticleSystem> systems = null;
            List<ParticleSystem> roots = null;
            EditorTimelineUtil.SetupEffectParticlePreview(effectGo, ref systems, ref roots);
            effectParticleRoots.Add(roots);
        }
    }

    private void UpdateEffectTransforms()
    {
        if (!TimelineClipPlayHitUtil.NeedPerFrameApply(thisConfig.UnitNodePointData))
        {
            return;
        }

        ForceApplyEffectTransforms();
    }

    /// <summary>按最新挂点配置回写所有受击特效实例（含不跟随时的 Inspector 微调）。</summary>
    private void ForceApplyEffectTransforms()
    {
        if (!InTime() || thisConfig.UnitNodePointData == null)
        {
            return;
        }

        var targets = control.PreviewBindings.GetTargets(config, true);
        var count = Mathf.Min(effectInstances.Count, targets.Count);
        var originPos = GetEffectSourceOriginPos();
        for (var i = 0; i < count; i++)
        {
            TimelineClipPlayHitUtil.ApplyEffectTransform(
                effectInstances[i].transform,
                thisConfig.UnitNodePointData,
                targets[i],
                originPos);
        }
    }

    private void SimulateParticles()
    {
        var sampleTiming = playTiming;
        if (thisConfig.AffectedByHitStop)
        {
            sampleTiming = TimelineEditorHitStopUtil.GetClipLogicPlayTimingForAnyHitStopClippedToRaw(
                control,
                GetContentTimelineStartTime(),
                rawHitContentDuration,
                (float)control.Timeline.RunningTime);
        }

        if (sampleTiming < 0f)
        {
            sampleTiming = 0f;
        }

        for (var i = 0; i < effectParticleRoots.Count; i++)
        {
            EditorTimelineUtil.SimulateEffectParticles(effectParticleRoots[i], sampleTiming);
        }
    }

    private void ClearEffectInstances()
    {
        for (var i = 0; i < effectInstances.Count; i++)
        {
            Object.DestroyImmediate(effectInstances[i]);
        }

        effectInstances.Clear();
        effectParticleRoots.Clear();
    }

    private void RefreshAnimationLength()
    {
        animationLength = 0f;
        if (!string.IsNullOrEmpty(thisConfig.AnimatorParamName))
        {
            var targets = control.PreviewBindings.GetTargets(config, true);
            if (targets.Count > 0)
            {
                var animator = targets[0].Animator;
                animationLength = GetAnimationLengthByName(animator, thisConfig.AnimatorParamName);
            }
        }

        // 纯动画自动模式：逻辑时长始终跟资源；有特效时至少盖住动画，但不从展示条长反推（避免定帧变短缩不回去）
        if (animationLength > 0f && ShouldAutoDurationFromAnimation())
        {
            rawHitContentDuration = animationLength;
            rawHitContentInitialized = true;
        }
        else if (animationLength > 0f)
        {
            if (!rawHitContentInitialized)
            {
                rawHitContentDuration = animationLength;
                rawHitContentInitialized = true;
            }
            else
            {
                rawHitContentDuration = Mathf.Max(rawHitContentDuration, animationLength);
            }
        }

        SyncHitDisplayDuration();
    }

    /// <summary>
    /// 用稳定的逻辑时长算展示条长；定帧变短时重叠变小，条长会收回。
    /// </summary>
    private void SyncHitDisplayDuration()
    {
        var contentStart = GetContentTimelineStartTime();
        if (!rawHitContentInitialized)
        {
            if (animationLength > 0f)
            {
                rawHitContentDuration = animationLength;
            }
            else if (thisConfig.AffectedByHitStop)
            {
                rawHitContentDuration = TimelineEditorHitStopUtil.GetRawDurationForAnyHitStop(
                    control,
                    contentStart,
                    Mathf.Max(config.Duration, 0.01f));
            }
            else
            {
                rawHitContentDuration = Mathf.Max(config.Duration, 0.01f);
            }

            rawHitContentDuration = Mathf.Max(rawHitContentDuration, 0.01f);
            rawHitContentInitialized = true;
        }

        if (animationLength > 0f && ShouldAutoDurationFromAnimation())
        {
            rawHitContentDuration = animationLength;
        }

        rawHitContentDuration = Mathf.Max(rawHitContentDuration, 0.01f);
        if (thisConfig.AffectedByHitStop)
        {
            config.Duration = TimelineEditorHitStopUtil.MeasureDisplayDurationForAnyHitStop(
                control,
                contentStart,
                rawHitContentDuration);
        }
        else
        {
            config.Duration = rawHitContentDuration;
        }

        lastWrittenDisplayDuration = config.Duration;
    }

    private static float GetAnimationLengthByName(Animator targetAnimator, string animationName)
    {
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null)
        {
            return 0f;
        }

        return EditorAnimatorStateUtil.GetStateLength(targetAnimator.runtimeAnimatorController, animationName);
    }

    private Vector3 GetEffectSourceOriginPos()
    {
        var source = control.PreviewBindings.GetSource(config);
        return source ? source.GetTransformPosition() : Vector3.zero;
    }

    private List<string> GetEnemyAnimatorClipNames()
    {
        var result = new List<string>();
        var targets = control.PreviewBindings.GetTargets(config, true);
        if (targets.Count == 0)
        {
            return result;
        }

        var animator = targets[0].Animator;
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return result;
        }

        EditorAnimatorStateUtil.CopyStateNames(animator.runtimeAnimatorController, result);
        return result;
    }

    /// <summary>预览目标变更时清理旧表现，重建动画列表及自动时长。</summary>
    public void RefreshPreviewTargets()
    {
        ChangeClipTimeStateType(ClipTimeStateType.OutTime);
        ClearEffectInstances();
        RefreshAnimationLength();
    }
}
