using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class TimelineClipPlayEffect : TimelineClipBase
{
    private List<ParticleSystem> particleSystems;
    private List<ParticleSystem> particleSystemRoots;

    private TimelineClipPlayEffectConfig thisConfig;

    private EffectPointLightData pointLightData;
    private Light effectPointLight;

    #region 播放动画

    private Animator effectAnimator;
    private int defaultStateHash;
    private float defaultClipLength;

    private void CacheEffectAnimator()
    {
        var path = AssetDatabase.GetAssetPath(effectAnimator.runtimeAnimatorController);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        var stateName = controller.layers[0].stateMachine.defaultState.name;
        defaultStateHash = Animator.StringToHash(stateName);
        defaultClipLength = GetClipLengthByName(effectAnimator, stateName);
    }

    private void PlayEffectAnimator()
    {
        var duration = config.Duration > 0f ? config.Duration : defaultClipLength;
        duration = Mathf.Max(duration, 0.01f);
        var sampleTiming = ResolveEffectSampleTiming();
        var normalizedTime = sampleTiming / duration;
        effectAnimator.speed = 0f;
        effectAnimator.Play(defaultStateHash, 0, normalizedTime);
        effectAnimator.Update(0f);
    }

    private static float GetClipLengthByName(Animator animator, string clipName)
    {
        var clips = animator.runtimeAnimatorController.animationClips;
        for (var i = 0; i < clips.Length; i++)
        {
            if (clips[i].name == clipName)
            {
                return clips[i].length;
            }
        }

        return 0f;
    }

    #endregion

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipPlayEffectConfig;
        if (!string.IsNullOrEmpty(thisConfig.EffectPath))
        {
            thisConfig.EffectPath = EditorTimelineAssetPathUtil.ToEffectLogicalName(thisConfig.EffectPath);
            prefabPath = thisConfig.EffectPath;
        }
        base.Init(control, config);
    }

    private List<UnitMono> ResolveSubjects()
    {
        return control.UnitBindings.ResolveUnits(config, thisConfig.Subject, string.Empty);
    }

    private void ApplyTransform()
    {
        var units = ResolveSubjects();
        instance.SetActive(units.Count > 0);
        if (units.Count == 0)
        {
            return;
        }

        var point = thisConfig.PointData;
        TimelineTransformUtil.GetTransformationFromUnitNodePointData(
            control.MyTransform.position, units, point, out var position, out var rotation, out var scale);
        var parent = TimelineTransformUtil.GetParentFromUnitNodePointData(point, units[0]);
        TimelineTransformUtil.ApplyEffectTransform(instance.transform, point, parent, position, rotation, scale, units[0]);
    }

    public override void DrawInspector()
    {
        base.DrawInspector();

        EditorGUI.BeginChangeCheck();
        var nextEffectPath = EditorGUILayout.TextField("逻辑名", thisConfig.EffectPath ?? string.Empty);
        var prefabObj = EditorTimelineAssetPathUtil.LoadEffectPrefab(nextEffectPath);
        var picked = (GameObject)EditorGUILayout.ObjectField("Prefab", prefabObj, typeof(GameObject), false);
        if (picked && picked != prefabObj)
        {
            nextEffectPath = EditorTimelineAssetPathUtil.EffectLogicalFromAssetPath(AssetDatabase.GetAssetPath(picked));
        }
        if (nextEffectPath != prefabPath)
        {
            ApplyEffectPath(nextEffectPath);
        }

        thisConfig.Subject = TimelineUnitBindingControl.DrawSimpleSubject(thisConfig.Subject);
        var point = thisConfig.PointData;
        point.OffsetNodeType = TimelineEnumDisplay.DrawNodePopup("挂点", point.OffsetNodeType);
        point.OffsetPos = EditorGUILayout.Vector3Field("位置偏移", point.OffsetPos);
        if (EditorGUI.EndChangeCheck())
        {
            // 在当前播放头重建特效，保留播放/暂停状态，偏移修改无需停止演出。
            control.SharedPlayback.Invalidate();
        }
    }

    private void ApplyEffectPath(string newPath)
    {
        var logical = EditorTimelineAssetPathUtil.ToEffectLogicalName(newPath);
        thisConfig.EffectPath = logical;
        prefabPath = logical;
        prefab = EditorTimelineAssetPathUtil.LoadEffectPrefab(logical);

        if (instance != null && !isBaseTransform)
        {
            UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            ChangeClipTimeStateType(ClipTimeStateType.OutTime);
        }
    }

    protected override void OnOutTime()
    {
        base.OnOutTime();
        particleSystems = null;
        particleSystemRoots = null;
        effectAnimator = null;
        pointLightData = null;
        effectPointLight = null;
    }

    protected override void OnInTime()
    {
        base.OnInTime();
        if (!instance)
        {
            return;
        }
        EditorTimelineUtil.SetupEffectParticlePreview(instance, ref particleSystems, ref particleSystemRoots);

        effectAnimator = instance.GetComponentInChildren<Animator>(true);
        if (effectAnimator != null)
        {
            CacheEffectAnimator();
        }

        BindPointLightPreview();
        ApplyTransform();
        ApplyPointLightAtPlayTime();
    }

    protected override void OnInTimeLogic()
    {
        base.OnInTimeLogic();
        if (!instance)
        {
            return;
        }
        if (NeedPerFrameApplyTransform())
        {
            ApplyTransform();
        }
        if (!instance.activeSelf)
        {
            return;
        }

        var sampleTime = ResolveEffectSampleTiming();
        EditorTimelineUtil.SimulateEffectParticles(particleSystemRoots, sampleTime);

        if (effectAnimator != null)
        {
            PlayEffectAnimator();
        }

        ApplyPointLightAtPlayTime();
    }

    /// <summary>受定帧影响时用逻辑时间采样；否则跟全局 playTiming。</summary>
    private float ResolveEffectSampleTiming()
    {
        if (!thisConfig.AffectedByHitStop)
        {
            return Mathf.Max(playTiming, 0f);
        }

        var contentStart = GetContentTimelineStartTime();
        var rawDuration = Mathf.Max(config.Duration, 0.01f);
        var runningTime = (float)control.Timeline.RunningTime;
        return TimelineEditorHitStopUtil.GetClipLogicPlayTimingForAnyHitStopClippedToRaw(
            control,
            contentStart,
            rawDuration,
            runningTime);
    }

    /// <summary>
    /// 挂载：仅「重置旋转 / 重置缩放」需每帧回写；未挂载：未勾「不跟随」则每帧跟查询点世界位姿。
    /// </summary>
    bool NeedPerFrameApplyTransform()
    {
        var pointData = thisConfig.PointData;
        if (pointData.NeedSetParent)
        {
            return pointData.UseOriginalRotation || pointData.UseOriginalScale;
        }

        return !pointData.DontFollow;
    }

    void BindPointLightPreview()
    {
        pointLightData = null;
        effectPointLight = null;
        if (string.IsNullOrEmpty(prefabPath))
        {
            return;
        }

        var assetPath = EditorTimelineAssetPathUtil.ResolveEffectPrefabAssetPath(prefabPath);
        if (string.IsNullOrEmpty(assetPath))
        {
            return;
        }

        var jsonPath = EffectPointLightUtil.GetJsonPathFromPrefab(assetPath);
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        var fullJson = Path.GetFullPath(Path.Combine(projectRoot, jsonPath));
        if (!File.Exists(fullJson))
        {
            return;
        }

        pointLightData = EffectPointLightJsonUtil.Load(jsonPath);
        effectPointLight = EffectPointLightUtil.FindPointLight(instance.transform);
    }

    void ApplyPointLightAtPlayTime()
    {
        if (pointLightData == null || effectPointLight == null)
        {
            return;
        }

        EffectPointLightUtil.ApplyAtTime(effectPointLight, pointLightData, ResolveEffectSampleTiming());
    }
}
