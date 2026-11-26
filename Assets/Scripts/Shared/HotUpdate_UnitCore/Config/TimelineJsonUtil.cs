using Newtonsoft.Json;
using UnityEngine;
using System;

/// <summary><see cref="TimelineConfig"/> 的 JSON 读写（Newtonsoft.Json）。</summary>
public static class TimelineJsonUtil
{
    /// <summary>将仅含配置字段的 JSON 反序列化为对应 <see cref="TimelineClipConfigBase"/>。</summary>
    public static TimelineClipConfigBase FromJson(TimelineClipType type, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var configType = GetConfigType(type);
        if (configType == null)
        {
            Debug.LogWarning($"UnitFeatureConfigJson 不支持的 Feature 类型: {type}");
            return null;
        }

        return (TimelineClipConfigBase)JsonConvert.DeserializeObject(json, configType, TimelineJsonUtil.ConcreteConfig);
    }

    /// <summary>将时间轴单段 JSON 解析为 <see cref="TimelineClipConfigBase"/>。</summary>
    public static TimelineClipConfigBase FromTimelineClipConfigJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonConvert.DeserializeObject<TimelineClipConfigBase>(json, TimelineJsonUtil.Default);
    }

    /// <summary>将配置序列化为 JSON（含 Type / StartTime / EndTime）。</summary>
    public static string ToJson(TimelineClipType type, TimelineClipConfigBase config, bool prettyPrint = false)
    {
        if (config == null)
            return string.Empty;

        var configType = GetConfigType(type);
        if (configType == null)
        {
            Debug.LogWarning($"UnitFeatureConfigJson 不支持的 Feature 类型: {type}");
            return string.Empty;
        }

        return JsonConvert.SerializeObject(config, configType, TimelineJsonUtil.ToFormatting(prettyPrint), TimelineJsonUtil.ConcreteConfig);
    }

    /// <summary>根据配置运行时类型序列化。</summary>
    public static string ToJson(TimelineClipConfigBase config, bool prettyPrint = false)
    {
        if (config == null)
            return string.Empty;

        return JsonConvert.SerializeObject(config, TimelineJsonUtil.ToFormatting(prettyPrint), TimelineJsonUtil.ConcreteConfig);
    }

    internal static Type GetConfigType(TimelineClipType type)
    {
        switch (type)
        {
            case TimelineClipType.PlayAnimator:
                return typeof(TimelineClipPlayAnimatorConfig);
            case TimelineClipType.SetCastTargets:
            {
                return typeof(TimelineClipSetCastTargetsConfig);
            }
            case TimelineClipType.SetHitTargets:
            {
                return typeof(TimelineClipSetHitTargetsConfig);
            }
            case TimelineClipType.PlaySpine:
            {
                return typeof(TimelineClipPlaySpineConfig);
            }
            case TimelineClipType.PlayEffect:
                return typeof(TimelineClipPlayEffectConfig);
            case TimelineClipType.PlayHit:
                return typeof(TimelineClipPlayHitConfig);
            case TimelineClipType.PlayBullet:
                return typeof(TimelineClipPlayBulletConfig);
            case TimelineClipType.Translate:
                return typeof(TimelineClipTranslateConfig);
            case TimelineClipType.Rotate:
                return typeof(TimelineClipRotateConfig);
            case TimelineClipType.Scale:
                return typeof(TimelineClipScaleConfig);
            case TimelineClipType.CameraShake:
                return typeof(TimelineClipCameraShakeConfig);
            case TimelineClipType.Force:
                return typeof(TimelineClipForceConfig);
            case TimelineClipType.Follow:
                return typeof(TimelineClipFollowConfig);
            case TimelineClipType.Move:
                return typeof(TimelineClipMoveConfig);
            case TimelineClipType.SceneDarken:
                return typeof(TimelineClipSceneDarkenConfig);
            case TimelineClipType.RoleRotateToTarget:
                return typeof(TimelineClipRoleRotateToTargetConfig);
            case TimelineClipType.HitStop:
                return typeof(TimelineClipHitStopConfig);
            case TimelineClipType.SetSkybox:
                return typeof(TimelineClipSetSkyboxConfig);
            case TimelineClipType.Visibility:
                return typeof(TimelineClipVisibilityConfig);
            case TimelineClipType.CameraPathAnim:
                return typeof(TimelineClipCameraPathAnimConfig);
            case TimelineClipType.HideBattleUI:
                return typeof(TimelineClipHideBattleUIConfig);
            case TimelineClipType.CameraShot:
                return typeof(TimelineClipCameraShotConfig);
            case TimelineClipType.CameraShotBlend:
                return typeof(TimelineClipCameraShotBlendConfig);
            default:
                return null;
        }
    }

    public static string ToJson(TimelineConfig config, bool prettyPrint = true)
    {
        if (config == null)
            return string.Empty;

        return JsonConvert.SerializeObject(
            config,
            ToFormatting(prettyPrint),
            Default);
    }

    public static TimelineConfig FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonConvert.DeserializeObject<TimelineConfig>(json, Default);
    }

    public static TimelineConfig FromTextAsset(TextAsset textAsset)
    {
        return textAsset == null ? null : FromJson(textAsset.text);
    }

    private static readonly UnityVector3JsonConverter Vector3Converter = new UnityVector3JsonConverter();

    public static readonly JsonSerializerSettings Default = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Converters = { new TimelineClipConfigConverter(), Vector3Converter },
    };

    /// <summary>反序列化具体 Config 子类时使用，避免 <see cref="TimelineClipConfigConverter"/> 递归。</summary>
    public static readonly JsonSerializerSettings ConcreteConfig = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Converters = { Vector3Converter },
    };

    public static Formatting ToFormatting(bool prettyPrint)
    {
        return prettyPrint ? Formatting.Indented : Formatting.None;
    }
}
