using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

/// <summary>校验编辑器文档和程序工程当前战斗播放器的兼容性。</summary>
internal static class TimelineEditorMcpValidation
{
    internal static (string ClipKey, TimelineEventConfigBase Point)[] GeneratedEvents(TimelineControl control)
    {
        return control.Clips.SelectMany(clip => clip.GetAllEvents()
            .Where(point => !clip.Events.Contains(point))
            .Select(point => (clip.Config.ClipKey, point.Config))).ToArray();
    }

    internal static object Report(TimelineConfig config, TimelineControl control) => new
    {
        editor = Result(() => RequireEditor(config, control)),
        battle = Battle(config, control.MyTransformPath)
    };

    internal static void RequireEditor(TimelineConfig config, TimelineControl control)
        => Editor(config, GeneratedEvents(control));

    internal static void Editor(TimelineConfig config, (string ClipKey, TimelineEventConfigBase Point)[] generated,
        bool normalizeTimes = false)
    {
        if (string.IsNullOrWhiteSpace(config.TimelineId))
        {
            throw new ArgumentException("TimelineId 不能为空。");
        }
        var clips = config.Clips.ToDictionary(clip => clip.ClipKey, StringComparer.Ordinal);
        var events = new Dictionary<string, (string ClipKey, TimelineEventConfigBase Point)>(StringComparer.Ordinal);
        foreach (var clip in config.Clips)
        {
            var previewTargets = clip is TimelineClipSetCastTargetsConfig castBinding ? castBinding.PreviewTargets
                : (clip as TimelineClipSetHitTargetsConfig)?.PreviewTargets;
            if (previewTargets is List<string>)
            {
                foreach (var key in previewTargets)
                {
                    TimelinePreviewActorKey.Validate(key);
                }
            }
            if (clip is TimelineClipSetHitTargetsConfig hitBinding)
            {
                TimelinePreviewActorKey.Validate(hitBinding.PreviewSource);
            }
            CheckTime(clip.ClipKey, "StartTime", clip.StartTime);
            CheckTime(clip.ClipKey, "DelayTime", clip.DelayTime);
            CheckTime(clip.ClipKey, "Duration", clip.Duration);
            if (clip.EventConfigs is List<TimelineEventConfigBase> points)
            {
                foreach (var point in points)
                {
                    CheckTime(point.EventKey, "Time", point.Time);
                    if (point.ValueType == ValueType.Percent && point.Time > 1)
                    {
                        throw new ArgumentException(point.EventKey + " 的比例时间必须在 0 到 1 之间。");
                    }
                    events.Add(point.EventKey, (clip.ClipKey, point));
                }
            }
        }
        foreach (var entry in generated)
        {
            if (clips.ContainsKey(entry.ClipKey) && !string.IsNullOrEmpty(entry.Point.EventKey))
            {
                events.TryAdd(entry.Point.EventKey, entry);
            }
        }
        CheckReference(clips, config.DurationBindClipKey, "DurationBindClipKey");
        foreach (var clip in config.Clips)
        {
            if (events.ContainsKey(clip.ClipKey))
            {
                throw new ArgumentException("ClipKey 与 EventKey 重复：" + clip.ClipKey);
            }
            if (!string.IsNullOrEmpty(clip.PreKey) && !events.ContainsKey(clip.PreKey))
            {
                CheckReference(clips, clip.PreKey, clip.ClipKey + ".PreKey");
            }
            CheckReference(clips, clip.EndByOtherClipEnd, clip.ClipKey + ".EndByOtherClipEnd");
            if (clip is TimelineClipPlayEffectConfig effect)
            {
                CheckSimpleSubject(effect.Subject, effect.ClipKey);
                if (effect.PointData is null)
                {
                    throw new ArgumentException(effect.ClipKey + ".PointData 不能为空。");
                }
            }
            else if (clip is TimelineClipHitStopConfig stop)
            {
                if (!Enum.IsDefined(typeof(TimelineUnitSubjectType), stop.Subject))
                {
                    throw new ArgumentException(stop.ClipKey + ".Subject 无效。");
                }
                if (stop.Subject != TimelineUnitSubjectType.Caster)
                {
                    CheckUnitBinding(clips, stop.TargetBindingKey, stop.Subject != TimelineUnitSubjectType.CastTargets);
                }
            }
            else if (clip is not TimelineClipVisibilityConfig)
            {
                CheckBinding(clips, clip.TargetBindingKey, clip.ClipKey + ".TargetBindingKey",
                    clip.Type is TimelineClipType.PlayHit or TimelineClipType.PlayBullet);
            }
            if (clip is TimelineClipPlayBulletConfig bulletFacing)
            {
                if (!Enum.IsDefined(typeof(TimelineBulletFacingType), bulletFacing.FacingType)
                    || float.IsNaN(bulletFacing.BillboardAngleOffset) || float.IsInfinity(bulletFacing.BillboardAngleOffset))
                {
                    throw new ArgumentException(clip.ClipKey + ".FacingType 或 BillboardAngleOffset 无效。");
                }
                CheckUnitBinding(clips, clip.TargetBindingKey, true);
            }
            if (clip is TimelineClipUnitConfigBase unit)
            {
                TimelineUnitClipPlayback.Validate(unit);
                if (unit.Subject != TimelineUnitSubjectType.Caster)
                {
                    CheckUnitBinding(clips, unit.SubjectBindingKey, unit.Subject != TimelineUnitSubjectType.CastTargets);
                }
            }
            if (clip is TimelineClipRoleRotateToTargetConfig face)
            {
                CheckUnitBinding(clips, face.DestinationBindingKey, face.Destination != TimelineMoveDestinationType.CastTargets);
            }
            if (clip is TimelineClipTranslateConfig translate)
            {
                CheckSimpleSubject(translate.Subject, translate.ClipKey);
                if (translate.Destination <= TimelineMoveDestinationType.EffectTargets)
                {
                    CheckUnitBinding(clips, translate.DestinationBindingKey, translate.Destination != TimelineMoveDestinationType.CastTargets);
                }
            }
            if (clip is TimelineClipCameraShotBlendConfig blend && !string.IsNullOrEmpty(blend.NextCameraShotClipKey)
                && (!clips.TryGetValue(blend.NextCameraShotClipKey, out var next) || next.Type != TimelineClipType.CameraShot))
            {
                throw new ArgumentException(clip.ClipKey + ".NextCameraShotClipKey 必须指向 CameraShot。");
            }
        }
        // 起点和终点分别求值。固定偏移事件只依赖父片段起点，百分比事件还依赖其长度。
        var active = new HashSet<(string Key, bool End)>();
        var times = new Dictionary<(string Key, bool End), float>();
        foreach (var clip in config.Clips)
        {
            var start = Resolve(clip.ClipKey, false);
            var end = Resolve(clip.ClipKey, true);
            if (end + 0.0001f < start)
            {
                throw new ArgumentException(clip.ClipKey + " 的绑定终点早于内容起点。");
            }
        }
        foreach (var clip in config.Clips)
        {
            if (clip is TimelineClipVisibilityConfig visibility)
            {
                CheckVisibility(visibility);
            }
            if (clip is TimelineClipCameraShotConfig shot)
            {
                if (shot.Use2DComposition && (float.IsNaN(shot.PlanarTravel) || float.IsInfinity(shot.PlanarTravel)))
                {
                    throw new ArgumentException(shot.ClipKey + ".PlanarTravel 必须是有限数。");
                }
                CheckPivot(shot, shot.LookPivot);
                CheckPivot(shot, shot.FromPivot);
            }
        }
        if (normalizeTimes)
        {
            foreach (var clip in config.Clips)
            {
                var start = times[(clip.ClipKey, false)];
                if (!string.IsNullOrEmpty(clip.PreKey))
                {
                    clip.StartTime = start - clip.DelayTime;
                }
                if (!string.IsNullOrEmpty(clip.EndByOtherClipEnd))
                {
                    clip.Duration = Math.Max(0f, times[(clip.ClipKey, true)] - start);
                }
            }
        }

        void CheckVisibility(TimelineClipVisibilityConfig visibility)
        {
            TimelineVisibilityPlayback.Validate(visibility);
            CheckVisibilityBinding(visibility.TargetBindingKey, false);
            CheckVisibilityBinding(visibility.EffectBindingKey, true);

            void CheckVisibilityBinding(string key, bool effect)
            {
                CheckUnitBinding(clips, key, effect);
                if (!string.IsNullOrEmpty(key)
                    && Resolve(key, false) > Resolve(visibility.ClipKey, false) + 0.0001f)
                {
                    throw new ArgumentException(visibility.ClipKey + " 的对象绑定晚于显隐起点：" + key);
                }
            }
        }

        void CheckPivot(TimelineClipCameraShotConfig shot, TimelineCameraPivotData pivot)
        {
            if (pivot is not null)
            {
                TimelineCameraPivotPlayback.Validate(pivot);
                if (pivot.Subject != TimelineUnitSubjectType.Caster)
                {
                    CheckUnitBinding(clips, pivot.BindingKey, pivot.Subject != TimelineUnitSubjectType.CastTargets);
                    if (!string.IsNullOrEmpty(pivot.BindingKey)
                        && Resolve(pivot.BindingKey, false) > Resolve(shot.ClipKey, false) + 0.0001f)
                    {
                        throw new ArgumentException(shot.ClipKey + " 的注视绑定晚于运镜起点。");
                    }
                }
            }
        }

        float Resolve(string key, bool end)
        {
            var vertex = (key, end);
            if (times.TryGetValue(vertex, out var time))
            {
                return time;
            }
            if (!active.Add(vertex))
            {
                throw new ArgumentException("连线或结束绑定形成循环：" + key);
            }
            var clip = clips[key];
            if (end)
            {
                time = string.IsNullOrEmpty(clip.EndByOtherClipEnd)
                    ? Resolve(key, false) + clip.Duration
                    : Resolve(clip.EndByOtherClipEnd, true);
            }
            else if (string.IsNullOrEmpty(clip.PreKey))
            {
                time = clip.StartTime + clip.DelayTime;
            }
            else if (events.TryGetValue(clip.PreKey, out var source))
            {
                var start = Resolve(source.ClipKey, false);
                var offset = source.Point.ValueType == ValueType.Percent
                    ? (Resolve(source.ClipKey, true) - start) * source.Point.Time
                    : TimelineSchedule.GetEventOffset(clips[source.ClipKey], source.Point);
                time = start + offset + clip.DelayTime;
            }
            else
            {
                time = Resolve(clip.PreKey, true) + clip.DelayTime;
            }
            CheckTime(key, end ? "contentEnd" : "contentStart", time);
            times[vertex] = time;
            active.Remove(vertex);
            return time;
        }

    }

    /// <summary>在改动画布前检查预览角色地址，空值按默认角色解析。</summary>
    internal static void ValidatePreviewCaster(TimelineConfig config)
    {
        var address = string.IsNullOrEmpty(config.PreviewCasterAddress)
            ? EditorTimelineAssetPathUtil.DefaultUnitLogicalName : config.PreviewCasterAddress;
        if (string.IsNullOrEmpty(EditorTimelineAssetPathUtil.ResolveUnitPrefabAssetPath(address)))
        {
            throw new ArgumentException(TimelineBindingEditorText.InvalidPreviewCaster + " " + address);
        }
    }

    internal static void ResolveSpineDurations(TimelineConfig config, string casterAddress)
    {
        var animatorClips = config.Clips.OfType<TimelineClipPlayAnimatorConfig>().ToArray();
        if (animatorClips.Length == 0)
        {
            return;
        }
        var defaultCaster = string.IsNullOrEmpty(casterAddress)
            ? EditorTimelineAssetPathUtil.DefaultUnitLogicalName
            : casterAddress;
        foreach (var clip in animatorClips)
        {
            var address = string.IsNullOrEmpty(clip.PrefabPath) ? defaultCaster : clip.PrefabPath;
            var prefab = EditorTimelineAssetPathUtil.LoadUnitPrefab(address);
            var animator = prefab ? prefab.GetComponentInChildren<Animator>(true) : null;
            if (!animator || !animator.runtimeAnimatorController)
            {
                throw new InvalidOperationException("找不到单位的 AnimatorController 数据：" + address);
            }
            if (string.IsNullOrWhiteSpace(clip.ParamName))
            {
                throw new ArgumentException(clip.ClipKey + ".ParamName 不能为空。");
            }
            var len = EditorAnimatorStateUtil.GetStateLength(animator.runtimeAnimatorController, clip.ParamName);
            if (len <= 0f)
            {
                throw new ArgumentException(clip.ClipKey + ".ParamName 在 AnimatorController 中不存在：" + clip.ParamName);
            }
            clip.AnimatorStateSpeed = EditorAnimatorStateUtil.GetStateSpeed(animator.runtimeAnimatorController, clip.ParamName);
            clip.Duration = len;
        }
    }

    internal static object Battle(TimelineConfig config, string casterAddress = null)
    {
        var configuration = JObject.FromObject(Result(() =>
        {
            TimelinePlayback.Validate(config);
            if (config.Clips.Count(clip => clip.Type == TimelineClipType.PlayHit) > 1)
            {
                throw new ArgumentException("当前战斗协议未提供命中序号，暂不支持多个 PlayHit；无 PlayHit 的辅助技能在演出结束时显示结果。");
            }
        }));
        return new
        {
            valid = (bool)configuration["valid"],
            error = (string)configuration["error"],
            scope = "configuration-only",
            resources = Result(() => CheckResources(config, casterAddress)),
            runtimeVerified = false,
            runtimeRequirements = "本地资源检查不代表构建包已更新。仍需在程序工程验证资源包地址、实际施法者动作及协议对象绑定。"
        };
    }

    internal static string Summary(object report)
    {
        var result = JObject.FromObject(report);
        return string.Format(TimelineBindingEditorText.ValidationSummary,
            Status(result["editor"]), Status(result["battle"]), Status(result["battle"]["resources"]));

        string Status(JToken check)
        {
            return (bool)check["valid"] ? TimelineBindingEditorText.Passed : (string)check["error"];
        }
    }

    private static void CheckResources(TimelineConfig config, string casterAddress)
    {
        // 动画时长解析会写配置，仅校验独立副本，避免“检查”修改画布。
        if (!string.IsNullOrEmpty(casterAddress))
        {
            try
            {
                ResolveSpineDurations(TimelineJsonUtil.FromJson(TimelineJsonUtil.ToJson(config)), casterAddress);
            }
            catch (InvalidOperationException error)
            {
                throw new ArgumentException(error.Message, error);
            }
        }
        var missing = new List<string>();
        foreach (var path in TimelinePlayback.EffectPaths(config))
        {
            if (string.IsNullOrEmpty(EditorTimelineAssetPathUtil.ResolveEffectPrefabAssetPath(path)))
            {
                missing.Add("Effect: " + path);
            }
            else
            {
                var effectPrefab = EditorTimelineAssetPathUtil.LoadEffectPrefab(path);
                var renderers = effectPrefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) missing.Add("Effect has no renderer: " + path);
                foreach (var renderer in renderers.Where(r => r.gameObject.activeSelf))
                {
                    var requiredMaterials = new List<Material>();
                    if (renderer is ParticleSystemRenderer particle)
                    {
                        // Trail-only 粒子合法地不设置主材质；未启用拖尾时也不要求第二槽。
                        if (particle.renderMode != ParticleSystemRenderMode.None) requiredMaterials.Add(particle.sharedMaterial);
                        if (particle.GetComponent<ParticleSystem>().trails.enabled) requiredMaterials.Add(particle.trailMaterial);
                    }
                    else requiredMaterials.AddRange(renderer.sharedMaterials);
                    foreach (var material in requiredMaterials)
                        if (!material || !material.shader || !material.shader.isSupported)
                            missing.Add("Effect material/shader: " + path + "/" + renderer.name);
                }
            }
        }
        foreach (var bullet in config.Clips.OfType<TimelineClipPlayBulletConfig>())
        {
            if (bullet.FacingType == TimelineBulletFacingType.CameraBillboard)
            {
                var prefab = EditorTimelineAssetPathUtil.LoadEffectPrefab(bullet.Path);
                if (!prefab || prefab.transform.childCount == 0)
                {
                    throw new ArgumentException(bullet.ClipKey + ": CameraBillboard 需要独立的视觉子节点。");
                }
            }
        }
        foreach (var skybox in config.Clips.OfType<TimelineClipSetSkyboxConfig>())
        {
            if (!string.IsNullOrEmpty(skybox.SkyboxMaterialPath)
                && !AssetDatabase.LoadAssetAtPath<Material>(skybox.SkyboxMaterialPath))
            {
                missing.Add("Skybox: " + skybox.SkyboxMaterialPath);
            }
        }
        if (missing.Count > 0)
        {
            throw new ArgumentException("当前预览画质下找不到本地资源：" + string.Join(", ", missing));
        }
    }

    private static object Result(Action validate)
    {
        try
        {
            validate();
            return new { valid = true, error = (string)null };
        }
        catch (ArgumentException error)
        {
            return new { valid = false, error = error.Message };
        }
    }

    private static void CheckTime(string key, string field, float value)
    {
        if (!(value >= 0f) || float.IsInfinity(value))
        {
            throw new ArgumentException(key + "." + field + " 必须是有限非负数。");
        }
    }

    private static void CheckReference(Dictionary<string, TimelineClipConfigBase> clips, string key, string field)
    {
        if (!string.IsNullOrEmpty(key) && !clips.ContainsKey(key))
        {
            throw new ArgumentException(field + " 引用了不存在的 Clip：" + key);
        }
    }

    private static void CheckUnitBinding(Dictionary<string, TimelineClipConfigBase> clips, string key, bool effect)
    {
        if (!string.IsNullOrEmpty(key) && (!clips.TryGetValue(key, out var binding)
            || (effect ? binding is not TimelineClipSetHitTargetsConfig : binding is not TimelineClipSetCastTargetsConfig)))
        {
            throw new ArgumentException("单位绑定引用不存在或类型不匹配：" + key);
        }
    }

    private static void CheckSimpleSubject(TimelineUnitSubjectType subject, string key)
    {
        if (subject is not (TimelineUnitSubjectType.Caster or TimelineUnitSubjectType.EffectSource or TimelineUnitSubjectType.EffectTargets))
        {
            throw new ArgumentException(key + ".Subject 仅支持 Caster、EffectSource 和 EffectTargets。");
        }
    }

    private static void CheckBinding(Dictionary<string, TimelineClipConfigBase> clips, string key, string field, bool hit = false)
    {
        if (!string.IsNullOrEmpty(key)
            && (!clips.TryGetValue(key, out var clip) || (clip.Type != TimelineClipType.SetCastTargets
                && clip.Type != TimelineClipType.SetHitTargets)))
        {
            throw new ArgumentException(field + " 必须指向目标绑定 Clip：" + key);
        }
        if (hit && !string.IsNullOrEmpty(key) && clips[key].Type != TimelineClipType.SetHitTargets)
        {
            throw new ArgumentException(field + " 必须指向效果来源和目标绑定：" + key);
        }
    }
}
