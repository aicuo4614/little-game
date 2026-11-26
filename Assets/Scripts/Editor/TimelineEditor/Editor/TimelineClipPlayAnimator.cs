using DG.Tweening;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class TimelineClipPlayAnimator : TimelineClipBase
{
    private Animator animator;
    /// <summary>墙钟时长：Clip.length / |State.speed|。</summary>
    private float animationLength;
    /// <summary>Animator 状态 Speed；负值表示倒放采样。</summary>
    private float stateSpeed = 1f;
    /// <summary>预览用 Motion Clip；负 Speed 时 Animator.Play scrub 会失效，改 SampleAnimation。</summary>
    private AnimationClip previewClip;
    private TimelineClipPlayAnimatorConfig thisConfig;

    public override void PreInit(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipPlayAnimatorConfig;
        if (thisConfig != null && !string.IsNullOrEmpty(thisConfig.PrefabPath))
        {
            thisConfig.PrefabPath = EditorTimelineAssetPathUtil.ToUnitLogicalName(thisConfig.PrefabPath);
            prefabPath = thisConfig.PrefabPath;
        }

        isBaseTransform = true;
        base.Init(control, config);

        if (prefab == null || thisConfig == null)
        {
            return;
        }

        var prefabAnimator = prefab.GetComponentInChildren<Animator>(true);
        RefreshAnimationLength(prefabAnimator);

        InitAnimationEvents();
    }

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        //base.Init(control, config);
    }

    public override void Draw()
    {
        if (thisConfig != null)
        {
            var contentStart = GetContentTimelineStartTime();
            if (thisConfig.AffectedByHitStop)
            {
                var selfUnit = control != null && control.MyTransform != null
                    ? control.MyTransform.GetComponent<UnitMono>()
                    : null;
                config.Duration = TimelineEditorHitStopUtil.MeasureDisplayDurationForUnit(
                    control,
                    selfUnit,
                    contentStart,
                    animationLength);
            }
            else
            {
                config.Duration = Mathf.Max(animationLength, 0f);
            }
        }

        base.Draw();
    }

    protected override void DrawEvents()
    {
        base.DrawEvents();
        for (var i = 0; i < animationEvents.Count; i++)
        {
            animationEvents[i].Draw();
        }
    }

    public override List<TimelineEventBase> GetAllEvents()
    {
        var results = new List<TimelineEventBase>();
        results.AddRange(base.GetAllEvents());
        results.AddRange(animationEvents);
        return results;
    }

    #region Inspector

    public override void DrawInspector()
    {
        base.DrawInspector();
        if (thisConfig == null)
        {
            return;
        }

        EditorGUILayout.LabelField("Play Animator", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(true);
        thisConfig.PrefabPath = EditorGUILayout.TextField("逻辑名", thisConfig.PrefabPath);
        EditorGUI.EndDisabledGroup();

        var prefabObj = EditorTimelineAssetPathUtil.LoadUnitPrefab(thisConfig.PrefabPath);
        var newPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab", prefabObj, typeof(GameObject), false);
        if (newPrefab != null)
        {
            var newPath = AssetDatabase.GetAssetPath(newPrefab);
            var logical = EditorTimelineAssetPathUtil.UnitLogicalFromAssetPath(newPath);
            if (logical != prefabPath)
            {
                ApplyPrefabPath(logical);
            }
        }

        DrawParamNameField();

        thisConfig.AffectedByHitStop = DrawAffectedByHitStopToggle(thisConfig.AffectedByHitStop);

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.FloatField("状态 Speed", stateSpeed);
        EditorGUILayout.FloatField("动画时长(含Speed)", animationLength);
        EditorGUILayout.FloatField(
            thisConfig.AffectedByHitStop ? "展示时长(含定帧)" : "展示时长",
            config.Duration);
        EditorGUI.EndDisabledGroup();
    }

    /// <summary>时长跟动画资源，快捷键不可改。</summary>
    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        return false;
    }

    private void DrawParamNameField()
    {
        var clipNames = GetAnimatorClipNames();
        if (clipNames.Count == 0)
        {
            EditorGUI.BeginChangeCheck();
            var nextParamName = EditorGUILayout.TextField("动画名", thisConfig.ParamName ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                thisConfig.ParamName = nextParamName;
                OnParamNameChanged();
            }

            return;
        }

        var currentIndex = 0;
        var paramName = thisConfig.ParamName ?? string.Empty;
        for (var i = 0; i < clipNames.Count; i++)
        {
            if (clipNames[i] == paramName)
            {
                currentIndex = i;
                break;
            }
        }

        EditorGUI.BeginChangeCheck();
        var newIndex = EditorGUILayout.Popup("动画名", currentIndex, clipNames.ToArray());
        if (EditorGUI.EndChangeCheck())
        {
            thisConfig.ParamName = clipNames[newIndex];
            OnParamNameChanged();
        }
    }

    private void ApplyPrefabPath(string newPath)
    {
        var logical = EditorTimelineAssetPathUtil.ToUnitLogicalName(newPath);
        thisConfig.PrefabPath = logical;
        prefabPath = logical;
        prefab = EditorTimelineAssetPathUtil.LoadUnitPrefab(logical);

        if (isBaseTransform)
        {
            instance = this.control.SetMyTransform(prefabPath).gameObject;
        }

        ChangeClipTimeStateType(ClipTimeStateType.OutTime);
        OnParamNameChanged();
        control.Timeline.StopPreView();
    }

    private void OnParamNameChanged()
    {
        if (prefab == null)
        {
            return;
        }

        var prefabAnimator = prefab.GetComponentInChildren<Animator>(true);
        RefreshAnimationLength(prefabAnimator);
        InitAnimationEvents();
        control.Timeline.Repaint();
    }

    #endregion

    #region On

    protected override void OnInTime()
    {
        base.OnInTime();
        if (instance == null || thisConfig == null)
        {
            return;
        }

        animator = instance.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            var paramName = thisConfig.ParamName ?? string.Empty;
            stateSpeed = EditorAnimatorStateUtil.GetStateSpeed(animator.runtimeAnimatorController, paramName);
            thisConfig.AnimatorStateSpeed = stateSpeed;
            previewClip = EditorAnimatorStateUtil.FindClipByStateName(animator.runtimeAnimatorController, paramName);
            if (animationLength <= 0f)
            {
                animationLength = EditorAnimatorStateUtil.GetStateLength(animator.runtimeAnimatorController, paramName);
            }
        }
    }

    protected override void OnInTimeLogic()
    {
        base.OnInTimeLogic();
        if (animator == null || previewClip == null || animationLength <= 0f)
        {
            return;
        }

        var selfUnit = instance != null ? instance.GetComponentInChildren<UnitMono>(true) : null;
        if (selfUnit == null && control.MyTransform != null)
        {
            selfUnit = control.MyTransform.GetComponent<UnitMono>();
        }

        var contentStart = GetContentTimelineStartTime();
        var logicTiming = thisConfig.AffectedByHitStop
            ? TimelineEditorHitStopUtil.GetClipLogicPlayTiming(
                control,
                selfUnit,
                contentStart,
                (float)control.Timeline.RunningTime)
            : playTiming;
        if (logicTiming < 0f)
        {
            logicTiming = 0f;
        }

        // 负 Speed 时 Animator.Play(normalizedTime) 无效（StateInfo.length=Infinity，姿态卡死）；
        // 改用 Clip 采样：墙钟进度 → Clip 时间，负 Speed 从尾扫到头。
        var progress = Mathf.Clamp01(logicTiming / animationLength);
        var clipTime = stateSpeed < 0f
            ? (1f - progress) * previewClip.length
            : progress * previewClip.length;
        SamplePreviewPose(clipTime);
    }

    protected override void OnOutTime()
    {
        RestoreAnimatorAfterSample();
        base.OnOutTime();
    }

    /// <summary>关闭 Animator 后按 Clip 时间写姿态，避免负 Speed 状态 Play scrub 失效。</summary>
    private void SamplePreviewPose(float clipTime)
    {
        if (animator == null || previewClip == null)
        {
            return;
        }

        if (animator.enabled)
        {
            animator.enabled = false;
        }

        previewClip.SampleAnimation(animator.gameObject, clipTime);
    }

    private void RestoreAnimatorAfterSample()
    {
        if (animator == null)
        {
            return;
        }

        if (!animator.enabled)
        {
            animator.enabled = true;
        }

        animator.speed = 0f;
    }

    #endregion

    #region 公共方法

    public float GetAnimationLengthByName(Animator targetAnimator, string animationName)
    {
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null)
        {
            return 0f;
        }

        return EditorAnimatorStateUtil.GetStateLength(targetAnimator.runtimeAnimatorController, animationName);
    }

    private List<string> GetAnimatorClipNames()
    {
        var result = new List<string>();
        if (prefab == null)
        {
            return result;
        }

        var prefabAnimator = prefab.GetComponentInChildren<Animator>(true);
        if (prefabAnimator == null || prefabAnimator.runtimeAnimatorController == null)
        {
            return result;
        }

        EditorAnimatorStateUtil.CopyStateNames(prefabAnimator.runtimeAnimatorController, result);
        return result;
    }

    private void RefreshAnimationLength(Animator prefabAnimator)
    {
        if (prefabAnimator == null || thisConfig == null)
        {
            animationLength = 0f;
            stateSpeed = 1f;
            previewClip = null;
            if (thisConfig != null)
            {
                thisConfig.AnimatorStateSpeed = 1f;
            }
            return;
        }

        var paramName = thisConfig.ParamName ?? string.Empty;
        var runtime = prefabAnimator.runtimeAnimatorController;
        stateSpeed = EditorAnimatorStateUtil.GetStateSpeed(runtime, paramName);
        thisConfig.AnimatorStateSpeed = stateSpeed;
        previewClip = EditorAnimatorStateUtil.FindClipByStateName(runtime, paramName);
        animationLength = GetAnimationLengthByName(prefabAnimator, paramName);
        config.Duration = animationLength;
    }

    private AnimationClip GetAnimationClipByName(Animator targetAnimator, string animationName)
    {
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null)
        {
            return null;
        }

        return EditorAnimatorStateUtil.FindClipByStateName(targetAnimator.runtimeAnimatorController, animationName);
    }

    #endregion

    public override void RemoveEvent(TimelineEventBase e)
    {
        base.RemoveEvent(e);
    }


    #region 动画事件逻辑

    private List<TimelineEventBase> animationEvents = new List<TimelineEventBase>();

    public void InitAnimationEvents()
    {
        animationEvents.Clear();
        if (prefab == null || thisConfig == null)
        {
            return;
        }

        var prefabAnimator = prefab.GetComponentInChildren<Animator>(true);
        if (prefabAnimator == null || prefabAnimator.runtimeAnimatorController == null)
        {
            return;
        }

        var animationClip = GetAnimationClipByName(prefabAnimator, thisConfig.ParamName);
        if (animationClip == null)
        {
            return;
        }

        var unityEvents = animationClip.events;
        if (unityEvents == null || unityEvents.Length == 0)
        {
            return;
        }

        for (var i = 0; i < unityEvents.Length; i++)
        {
            var animEvent = unityEvents[i];
            var eventConfig = CreateEventConfigFromAnimationEvent(
                animEvent,
                i,
                animationClip.length,
                config.Duration,
                stateSpeed);
            animationEvents.Add(CreateEvent(eventConfig));
        }
    }

    /// <summary>
    /// 事件墙钟时间：正 Speed 按 clip 进度映射；负 Speed 倒放，从尾倒推。
    /// </summary>
    private TimelineEventConfigBase CreateEventConfigFromAnimationEvent(
        AnimationEvent animEvent,
        int index,
        float clipLength,
        float clipDuration,
        float animStateSpeed)
    {
        var clipTime = animEvent.time;
        if (animStateSpeed < 0f)
        {
            clipTime = clipLength - animEvent.time;
        }

        var config = new TimelineEventConfigBase
        {
            TimelineEventType = TimelineEventType.AnimationEvent,
            ValueType = ValueType.Normal,
            EventName = animEvent.functionName,
            Time = clipLength > 0f ? clipTime / clipLength * clipDuration : 0f,
            EventKey = this.config.ClipKey + "_" + animEvent.functionName + $"_{index}",
        };

        //if (this.thisConfig.AnimatorEventDatas != null)
        //{
        //    var data = this.thisConfig.AnimatorEventDatas.Find(o => o.EventName == animEvent.functionName && this.thisConfig.AnimatorEventDatas.IndexOf(o) == index);
        //    if (data != null)
        //    {
        //        config.ToClipKey = data.LinkClipKey;
        //    }
        //}

        return config;
    }

    #endregion


    public override bool SyncEventConfigsBeforeSave()
    {
        if (!base.SyncEventConfigsBeforeSave())
        {
            return false;
        }
        //if (!CheckCreateTimelineClipPlayAnimatorEventDatas(out var newDatas))
        //{
        //    return false;
        //}
        //this.thisConfig.AnimatorEventDatas = newDatas;
        return true;
    }

    protected override void AdjustStartTimeByPreClipKeyLogic()
    {
        base.AdjustStartTimeByPreClipKeyLogic();

    }


    //public bool CheckCreateTimelineClipPlayAnimatorEventDatas(out List<TimelineClipPlayAnimatorEventData> newDatas)
    //{
    //    newDatas = new List<TimelineClipPlayAnimatorEventData>();
    //    for (int i = 0; i < this.animationEvents.Count; i++)
    //    {
    //        var animatorEvent = this.animationEvents[i];
    //        if (!string.IsNullOrEmpty(animatorEvent.Config.ToClipKey) && !string.IsNullOrEmpty(animatorEvent.Config.EventName))
    //        {
    //            newDatas.Add(new TimelineClipPlayAnimatorEventData()
    //            {
    //                EventName = animatorEvent.Config.EventName,
    //                LinkClipKey = animatorEvent.Config.ToClipKey,
    //            });
    //        }
    //    }


    //    var oldDatas = thisConfig.AnimatorEventDatas ?? new List<TimelineClipPlayAnimatorEventData>();
    //    if (oldDatas.Count != newDatas.Count)
    //    {
    //        UnityEngine.Debug.LogError("保存失败 原因：你使用了新的模型，但是新的模型的动画事件和原始模型的动画事件数量不一致，请检查");
    //        return false;
    //    }

    //    List<string> oldEventNames = oldDatas.Select(item => item.EventName).ToList();
    //    List<string> newEventNames = newDatas.Select(item => item.EventName).ToList();
    //    if (!AreSameIgnoringOrder(oldEventNames, newEventNames))
    //    {
    //        UnityEngine.Debug.LogError("保存失败 原因：由于你可能使用了新的模型，新的模型的动画事件和旧的模型不一致，请检查");
    //        return false;
    //    }
    //    return true;
    //}

    private bool AreSameIgnoringOrder(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        var sortedA = a.OrderBy(x => x).ToList();
        var sortedB = b.OrderBy(x => x).ToList();
        return sortedA.SequenceEqual(sortedB);
    }

    public override void OnSetMyTransformPrefabPath(string path)
    {
        base.OnSetMyTransformPrefabPath(path);
        if (thisConfig != null)
        {
            thisConfig.PrefabPath = EditorTimelineAssetPathUtil.ToUnitLogicalName(path);
        }

        // 换预览阵营会 Destroy 旧施法者；清缓存，下次 OnInTime 从新 instance 取 Animator
        animator = null;
        ChangeClipTimeStateType(ClipTimeStateType.OutTime);
    }

    protected override GameObject LoadClipPrefab(string logicalName)
    {
        return EditorTimelineAssetPathUtil.LoadUnitPrefab(logicalName);
    }

    public override void OnStopPreview()
    {
        if (instance == null)
        {
            base.OnStopPreview();
            return;
        }

        var a = instance.GetComponentInChildren<Animator>(true);
        if (a != null)
        {
            a.gameObject.SetActive(true);
            a.enabled = true;
            if (a.isActiveAndEnabled)
            {
                a.speed = 0f;
                a.Play(Animator.StringToHash("Idle"), 0, 0f);
                a.Update(0f);
            }
        }

        base.OnStopPreview();
    }
    public override void OnDestroy()
    {
        base.OnDestroy();
    }
}
