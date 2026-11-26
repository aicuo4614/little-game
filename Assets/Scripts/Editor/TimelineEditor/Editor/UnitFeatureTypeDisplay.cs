using System.Collections.Generic;
using UnityEngine;

/// <summary>Feature 类型在编辑器中的中文显示名。</summary>
public static class UnitFeatureTypeDisplay
{
    private static readonly TimelineClipType[] AllTypes =
    {
        TimelineClipType.Translate,
        TimelineClipType.Rotate,
        TimelineClipType.Scale,
        TimelineClipType.PlayAnimator,
        TimelineClipType.SetCastTargets,
        TimelineClipType.SetHitTargets,
        TimelineClipType.PlayBullet,
        TimelineClipType.PlayEffect,
        TimelineClipType.PlayHit,
        TimelineClipType.CameraShake,
        TimelineClipType.CameraPathAnim,
        TimelineClipType.CameraShot,
        TimelineClipType.CameraShotBlend,
        TimelineClipType.Follow,
        TimelineClipType.Move,
        TimelineClipType.Force,
        TimelineClipType.SceneDarken,
        TimelineClipType.RoleRotateToTarget,
        TimelineClipType.HitStop,
        TimelineClipType.SetSkybox,
        TimelineClipType.Visibility,
        TimelineClipType.HideBattleUI,
    };

    private static string[] typeLabels;

    public static IReadOnlyList<TimelineClipType> Types => AllTypes;

    public static string GetDisplayName(TimelineClipType type)
    {
        switch (type)
        {
            case TimelineClipType.Translate:
                return "位移";
            case TimelineClipType.Rotate:
                return "旋转";
            case TimelineClipType.Scale:
                return "缩放";
            case TimelineClipType.SetCastTargets:
            {
                return TimelineBindingEditorText.CastObjects;
            }
            case TimelineClipType.SetHitTargets:
            {
                return TimelineBindingEditorText.EffectObjects;
            }
            case TimelineClipType.PlaySpine:
            {
                return "Spine";
            }
            case TimelineClipType.PlayAnimator:
                return "播放动画";
            case TimelineClipType.PlayBullet:
                return "播放子弹";
            case TimelineClipType.PlayEffect:
                return "播放普通特效";
            case TimelineClipType.PlayHit:
                return "播放受击";
            case TimelineClipType.CameraShake:
                return "震动";
            case TimelineClipType.CameraPathAnim:
                return "运动轨迹动画";
            case TimelineClipType.CameraShot:
                return "运镜";
            case TimelineClipType.CameraShotBlend:
                return "运镜过渡";
            case TimelineClipType.Follow:
                return "跟随";
            case TimelineClipType.Move:
                return "移动";
            case TimelineClipType.Force:
                return "施加力";
            case TimelineClipType.SceneDarken:
                return "场景压暗";
            case TimelineClipType.RoleRotateToTarget:
                return "朝向目标旋转";
            case TimelineClipType.HitStop:
                return "角色定帧";
            case TimelineClipType.SetSkybox:
                return "换天空盒";
            case TimelineClipType.Visibility:
                return "显隐";
            case TimelineClipType.HideBattleUI:
                return "隐藏战场UI";
            default:
                return type.ToString();
        }
    }

    /// <summary>时间轴 Clip 块上显示的名称（含父级分类前缀）。</summary>
    public static string GetClipLabelName(TimelineClipConfigBase config)
    {
        if (config == null)
        {
            return "Clip";
        }

        var name = GetDisplayName(config.Type);
        switch (config.Type)
        {
            case TimelineClipType.Translate:
            case TimelineClipType.Rotate:
            case TimelineClipType.Scale:
            case TimelineClipType.PlayAnimator:
            case TimelineClipType.RoleRotateToTarget:
                return "单位 " + name;
            case TimelineClipType.Move:
                return GetUniversalMoveFollowLabelName(name, ((TimelineClipMoveConfig)config).MoveEntityType);
            case TimelineClipType.Follow:
                return GetUniversalMoveFollowLabelName(name, ((TimelineClipFollowConfig)config).MoveEntityType);
            case TimelineClipType.CameraShake:
            case TimelineClipType.CameraPathAnim:
            case TimelineClipType.CameraShotBlend:
                return "相机 " + name;
            case TimelineClipType.CameraShot:
            {
                var shot = (TimelineClipCameraShotConfig)config;
                return "相机 " + name + " " + TimelineEnumDisplay.GetShortDisplayName(shot.ShotType);
            }
            case TimelineClipType.Visibility:
                return "通用 " + name;
            case TimelineClipType.SetSkybox:
                return "场景 " + name;
            case TimelineClipType.HideBattleUI:
                return "UI " + name;
            default:
                return name;
        }
    }

    private static string GetUniversalMoveFollowLabelName(string name, TimelineMoveEntityType moveEntityType)
    {
        return "通用 " + GetMoveEntityTypeDisplayName(moveEntityType) + " " + name;
    }

    private static string GetMoveEntityTypeDisplayName(TimelineMoveEntityType moveEntityType)
    {
        return TimelineMoveTypeDisplay.GetDisplayName(moveEntityType);
    }

    public static string GetAddClipMenuPath(TimelineClipType type)
    {
        var name = GetDisplayName(type);
        if (type is TimelineClipType.CameraPathAnim
            or TimelineClipType.Move or TimelineClipType.Follow or TimelineClipType.Force)
        {
            name += TimelineBindingEditorText.PreviewOnlySuffix;
        }
        switch (type)
        {
            case TimelineClipType.Translate:
            case TimelineClipType.Rotate:
            case TimelineClipType.Scale:
            case TimelineClipType.PlayAnimator:
            case TimelineClipType.RoleRotateToTarget:
                return "添加Clip/单位/" + name;
            case TimelineClipType.CameraShake:
            case TimelineClipType.CameraPathAnim:
            case TimelineClipType.CameraShot:
            case TimelineClipType.CameraShotBlend:
                return "添加Clip/相机/" + name;
            case TimelineClipType.Move:
            case TimelineClipType.Follow:
            case TimelineClipType.Visibility:
                return "添加Clip/通用/" + name;
            case TimelineClipType.SetSkybox:
            case TimelineClipType.SceneDarken:
                return "添加Clip/场景/" + name;
            case TimelineClipType.HideBattleUI:
                return "添加Clip/UI/" + name;
            default:
                return "添加Clip/" + name;
        }
    }

    public static string[] GetTypeLabels()
    {
        if (typeLabels != null && typeLabels.Length == AllTypes.Length)
        {
            return typeLabels;
        }

        typeLabels = new string[AllTypes.Length];
        for (var i = 0; i < AllTypes.Length; i++)
        {
            typeLabels[i] = GetDisplayName(AllTypes[i]);
        }

        return typeLabels;
    }

    public static int IndexOf(TimelineClipType type)
    {
        for (var i = 0; i < AllTypes.Length; i++)
        {
            if (AllTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    public static TimelineClipType TypeAt(int index)
    {
        if (index < 0 || index >= AllTypes.Length)
        {
            return AllTypes[0];
        }

        return AllTypes[index];
    }
}
