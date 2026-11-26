using UnityEditor;

/// <summary>技能编辑器 Inspector 常用枚举的中文显示（不含 DOTween Ease）。</summary>
public static class TimelineEnumDisplay
{
    private static readonly DurationType[] DurationTypes =
    {
        DurationType.Time,
        DurationType.Speed,
    };

    private static readonly HitTranslateType[] HitTranslateTypes =
    {
        HitTranslateType.Attack,
        HitTranslateType.HitBack,
        HitTranslateType.HitFly,
        HitTranslateType.None,
    };

    private static readonly UnitNodeType[] NodeTypes =
    {
        UnitNodeType.None,
        UnitNodeType.Transform,
        UnitNodeType.Root,
        UnitNodeType.LeftHand,
        UnitNodeType.RightHand,
        UnitNodeType.Spine,
        UnitNodeType.Head,
        UnitNodeType.Custom1,
        UnitNodeType.Custom2,
        UnitNodeType.Custom3,
        UnitNodeType.GroupCenter,
        UnitNodeType.World,
        UnitNodeType.UnitOriginPos,
    };

    private static readonly TimelineEventType[] EventTypes =
    {
        TimelineEventType.CustomEvent,
        TimelineEventType.AnimationEvent,
    };

    private static readonly ValueType[] ValueTypes =
    {
        ValueType.Normal,
        ValueType.Percent,
    };

    private static readonly TimelineBulletTrajectoryType[] BulletTrajectoryTypes =
    {
        TimelineBulletTrajectoryType.Parabola,
        TimelineBulletTrajectoryType.Linear,
    };

    private static readonly TimelineCameraShakeEnvelopeType[] CameraShakeEnvelopeTypes =
    {
        TimelineCameraShakeEnvelopeType.FadeOut,
        TimelineCameraShakeEnvelopeType.Constant,
        TimelineCameraShakeEnvelopeType.FadeIn,
        TimelineCameraShakeEnvelopeType.FadeInOut,
    };

    private static readonly TimelineCameraShotType[] CameraShotTypes =
    {
        TimelineCameraShotType.PushIn,
        TimelineCameraShotType.PullOut,
        TimelineCameraShotType.Orbit,
        TimelineCameraShotType.BoomUp,
        TimelineCameraShotType.BoomDown,
        TimelineCameraShotType.DollyBoom,
        TimelineCameraShotType.Truck,
        TimelineCameraShotType.Helix,
        TimelineCameraShotType.PivotBlend,
        TimelineCameraShotType.Hold,
        TimelineCameraShotType.WhipPan,
        TimelineCameraShotType.DollyZoom,
        TimelineCameraShotType.Ots,
        TimelineCameraShotType.Crane,
    };

    private static readonly TimelineCameraShotLookAtType[] CameraShotLookAtTypes =
    {
        TimelineCameraShotLookAtType.Self,
        TimelineCameraShotLookAtType.Target,
        TimelineCameraShotLookAtType.SelfTargetCenter,
    };

    private static string[] durationLabels;
    private static string[] hitTranslateLabels;
    private static string[] nodeLabels;
    private static string[] eventLabels;
    private static string[] valueLabels;
    private static string[] bulletTrajectoryLabels;
    private static string[] cameraShakeEnvelopeLabels;
    private static string[] cameraShotLabels;
    private static string[] cameraShotLookAtLabels;

    public static string GetDisplayName(DurationType type)
    {
        switch (type)
        {
            case DurationType.Time:
                return "时间";
            case DurationType.Speed:
                return "速度";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(HitTranslateType type)
    {
        switch (type)
        {
            case HitTranslateType.Attack:
                return "攻击";
            case HitTranslateType.HitBack:
                return "击退";
            case HitTranslateType.HitFly:
                return "击飞";
            case HitTranslateType.None:
                return "无";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(UnitNodeType type)
    {
        switch (type)
        {
            case UnitNodeType.None:
                return "无(None)";
            case UnitNodeType.Transform:
                return "自身(Transform)";
            case UnitNodeType.Root:
                return "根节点(Root)";
            case UnitNodeType.LeftHand:
                return "左手(LeftHand)";
            case UnitNodeType.RightHand:
                return "右手(RightHand)";
            case UnitNodeType.Spine:
                return "脊柱(Spine)";
            case UnitNodeType.Head:
                return "头部(Head)";
            case UnitNodeType.Custom1:
                return "自定义1(Custom1)";
            case UnitNodeType.Custom2:
                return "自定义2(Custom2)";
            case UnitNodeType.Custom3:
                return "自定义3(Custom3)";
            case UnitNodeType.GroupCenter:
                return "队伍中心(GroupCenter)";
            case UnitNodeType.World:
                return "世界(World)";
            case UnitNodeType.UnitOriginPos:
                return "单位原点(UnitOriginPos)";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(TimelineEventType type)
    {
        switch (type)
        {
            case TimelineEventType.CustomEvent:
                return "自定义事件";
            case TimelineEventType.AnimationEvent:
                return "动画事件";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(ValueType type)
    {
        switch (type)
        {
            case ValueType.Normal:
                return "绝对时间";
            case ValueType.Percent:
                return "百分比";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(TimelineBulletTrajectoryType type)
    {
        switch (type)
        {
            case TimelineBulletTrajectoryType.Parabola:
                return "抛物线";
            case TimelineBulletTrajectoryType.Linear:
                return "直线";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(TimelineCameraShakeEnvelopeType type)
    {
        switch (type)
        {
            case TimelineCameraShakeEnvelopeType.FadeOut:
                return "衰减(FadeOut)";
            case TimelineCameraShakeEnvelopeType.Constant:
                return "恒定(Constant)";
            case TimelineCameraShakeEnvelopeType.FadeIn:
                return "增强(FadeIn)";
            case TimelineCameraShakeEnvelopeType.FadeInOut:
                return "先强后弱(FadeInOut)";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(TimelineCameraShotType type)
    {
        switch (type)
        {
            case TimelineCameraShotType.PushIn:
                return "推近(PushIn)";
            case TimelineCameraShotType.PullOut:
                return "拉远(PullOut)";
            case TimelineCameraShotType.Orbit:
                return "环绕(Orbit)";
            case TimelineCameraShotType.BoomUp:
                return "升起(BoomUp)";
            case TimelineCameraShotType.BoomDown:
                return "降下(BoomDown)";
            case TimelineCameraShotType.DollyBoom:
                return "推拉升降环绕(DollyBoom)";
            case TimelineCameraShotType.Truck:
                return "横移(Truck)";
            case TimelineCameraShotType.Helix:
                return "螺旋(Helix)";
            case TimelineCameraShotType.PivotBlend:
                return "换枢轴(PivotBlend)";
            case TimelineCameraShotType.Hold:
                return "定格(Hold)";
            case TimelineCameraShotType.WhipPan:
                return "甩镜(WhipPan)";
            case TimelineCameraShotType.DollyZoom:
                return "变焦推(DollyZoom)";
            case TimelineCameraShotType.Ots:
                return "过肩(OTS)";
            case TimelineCameraShotType.Crane:
                return "起重机(Crane)";
            default:
                return type.ToString();
        }
    }

    /// <summary>Clip 块短名。</summary>
    public static string GetShortDisplayName(TimelineCameraShotType type)
    {
        switch (type)
        {
            case TimelineCameraShotType.PushIn:
                return "推近";
            case TimelineCameraShotType.PullOut:
                return "拉远";
            case TimelineCameraShotType.Orbit:
                return "环绕";
            case TimelineCameraShotType.BoomUp:
                return "升起";
            case TimelineCameraShotType.BoomDown:
                return "降下";
            case TimelineCameraShotType.DollyBoom:
                return "推拉升降环绕";
            case TimelineCameraShotType.Truck:
                return "横移";
            case TimelineCameraShotType.Helix:
                return "螺旋";
            case TimelineCameraShotType.PivotBlend:
                return "换枢轴";
            case TimelineCameraShotType.Hold:
                return "定格";
            case TimelineCameraShotType.WhipPan:
                return "甩镜";
            case TimelineCameraShotType.DollyZoom:
                return "变焦推";
            case TimelineCameraShotType.Ots:
                return "过肩";
            case TimelineCameraShotType.Crane:
                return "起重机";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(TimelineCameraShotLookAtType type)
    {
        switch (type)
        {
            case TimelineCameraShotLookAtType.Self:
                return "自身";
            case TimelineCameraShotLookAtType.Target:
                return "目标";
            case TimelineCameraShotLookAtType.SelfTargetCenter:
                return "自身目标中点";
            case TimelineCameraShotLookAtType.SelfOffset:
                return "自身";
            default:
                return type.ToString();
        }
    }

    public static DurationType DrawDurationPopup(string label, DurationType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetDurationLabels());
        return DurationTypeAt(index);
    }

    public static HitTranslateType DrawHitTranslatePopup(string label, HitTranslateType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetHitTranslateLabels());
        return HitTranslateTypeAt(index);
    }

    public static UnitNodeType DrawNodePopup(string label, UnitNodeType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetNodeLabels());
        return NodeTypeAt(index);
    }

    public static TimelineEventType DrawEventPopup(string label, TimelineEventType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetEventLabels());
        return EventTypeAt(index);
    }

    public static ValueType DrawValuePopup(string label, ValueType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetValueLabels());
        return ValueTypeAt(index);
    }

    public static TimelineBulletTrajectoryType DrawBulletTrajectoryPopup(
        string label,
        TimelineBulletTrajectoryType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetBulletTrajectoryLabels());
        return BulletTrajectoryTypeAt(index);
    }

    public static TimelineCameraShakeEnvelopeType DrawCameraShakeEnvelopePopup(
        string label,
        TimelineCameraShakeEnvelopeType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetCameraShakeEnvelopeLabels());
        return CameraShakeEnvelopeTypeAt(index);
    }

    public static TimelineCameraShotType DrawCameraShotTypePopup(string label, TimelineCameraShotType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetCameraShotLabels());
        return CameraShotTypeAt(index);
    }

    public static TimelineCameraShotLookAtType DrawCameraShotLookAtPopup(
        string label,
        TimelineCameraShotLookAtType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetCameraShotLookAtLabels());
        return CameraShotLookAtTypeAt(index);
    }

    private static string[] GetDurationLabels()
    {
        if (durationLabels != null && durationLabels.Length == DurationTypes.Length)
        {
            return durationLabels;
        }

        durationLabels = new string[DurationTypes.Length];
        for (var i = 0; i < DurationTypes.Length; i++)
        {
            durationLabels[i] = GetDisplayName(DurationTypes[i]);
        }

        return durationLabels;
    }

    private static string[] GetHitTranslateLabels()
    {
        if (hitTranslateLabels != null && hitTranslateLabels.Length == HitTranslateTypes.Length)
        {
            return hitTranslateLabels;
        }

        hitTranslateLabels = new string[HitTranslateTypes.Length];
        for (var i = 0; i < HitTranslateTypes.Length; i++)
        {
            hitTranslateLabels[i] = GetDisplayName(HitTranslateTypes[i]);
        }

        return hitTranslateLabels;
    }

    private static string[] GetNodeLabels()
    {
        if (nodeLabels != null && nodeLabels.Length == NodeTypes.Length)
        {
            return nodeLabels;
        }

        nodeLabels = new string[NodeTypes.Length];
        for (var i = 0; i < NodeTypes.Length; i++)
        {
            nodeLabels[i] = GetDisplayName(NodeTypes[i]);
        }

        return nodeLabels;
    }

    private static string[] GetEventLabels()
    {
        if (eventLabels != null && eventLabels.Length == EventTypes.Length)
        {
            return eventLabels;
        }

        eventLabels = new string[EventTypes.Length];
        for (var i = 0; i < EventTypes.Length; i++)
        {
            eventLabels[i] = GetDisplayName(EventTypes[i]);
        }

        return eventLabels;
    }

    private static string[] GetValueLabels()
    {
        if (valueLabels != null && valueLabels.Length == ValueTypes.Length)
        {
            return valueLabels;
        }

        valueLabels = new string[ValueTypes.Length];
        for (var i = 0; i < ValueTypes.Length; i++)
        {
            valueLabels[i] = GetDisplayName(ValueTypes[i]);
        }

        return valueLabels;
    }

    private static string[] GetBulletTrajectoryLabels()
    {
        if (bulletTrajectoryLabels != null && bulletTrajectoryLabels.Length == BulletTrajectoryTypes.Length)
        {
            return bulletTrajectoryLabels;
        }

        bulletTrajectoryLabels = new string[BulletTrajectoryTypes.Length];
        for (var i = 0; i < BulletTrajectoryTypes.Length; i++)
        {
            bulletTrajectoryLabels[i] = GetDisplayName(BulletTrajectoryTypes[i]);
        }

        return bulletTrajectoryLabels;
    }

    private static string[] GetCameraShakeEnvelopeLabels()
    {
        if (cameraShakeEnvelopeLabels != null
            && cameraShakeEnvelopeLabels.Length == CameraShakeEnvelopeTypes.Length)
        {
            return cameraShakeEnvelopeLabels;
        }

        cameraShakeEnvelopeLabels = new string[CameraShakeEnvelopeTypes.Length];
        for (var i = 0; i < CameraShakeEnvelopeTypes.Length; i++)
        {
            cameraShakeEnvelopeLabels[i] = GetDisplayName(CameraShakeEnvelopeTypes[i]);
        }

        return cameraShakeEnvelopeLabels;
    }

    private static string[] GetCameraShotLabels()
    {
        if (cameraShotLabels != null && cameraShotLabels.Length == CameraShotTypes.Length)
        {
            return cameraShotLabels;
        }

        cameraShotLabels = new string[CameraShotTypes.Length];
        for (var i = 0; i < CameraShotTypes.Length; i++)
        {
            cameraShotLabels[i] = GetDisplayName(CameraShotTypes[i]);
        }

        return cameraShotLabels;
    }

    private static string[] GetCameraShotLookAtLabels()
    {
        if (cameraShotLookAtLabels != null && cameraShotLookAtLabels.Length == CameraShotLookAtTypes.Length)
        {
            return cameraShotLookAtLabels;
        }

        cameraShotLookAtLabels = new string[CameraShotLookAtTypes.Length];
        for (var i = 0; i < CameraShotLookAtTypes.Length; i++)
        {
            cameraShotLookAtLabels[i] = GetDisplayName(CameraShotLookAtTypes[i]);
        }

        return cameraShotLookAtLabels;
    }

    private static int IndexOf(DurationType type)
    {
        for (var i = 0; i < DurationTypes.Length; i++)
        {
            if (DurationTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(HitTranslateType type)
    {
        for (var i = 0; i < HitTranslateTypes.Length; i++)
        {
            if (HitTranslateTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(UnitNodeType type)
    {
        for (var i = 0; i < NodeTypes.Length; i++)
        {
            if (NodeTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(TimelineEventType type)
    {
        for (var i = 0; i < EventTypes.Length; i++)
        {
            if (EventTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(ValueType type)
    {
        for (var i = 0; i < ValueTypes.Length; i++)
        {
            if (ValueTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(TimelineBulletTrajectoryType type)
    {
        for (var i = 0; i < BulletTrajectoryTypes.Length; i++)
        {
            if (BulletTrajectoryTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(TimelineCameraShakeEnvelopeType type)
    {
        for (var i = 0; i < CameraShakeEnvelopeTypes.Length; i++)
        {
            if (CameraShakeEnvelopeTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static DurationType DurationTypeAt(int index)
    {
        if (index < 0 || index >= DurationTypes.Length)
        {
            return DurationTypes[0];
        }

        return DurationTypes[index];
    }

    private static HitTranslateType HitTranslateTypeAt(int index)
    {
        if (index < 0 || index >= HitTranslateTypes.Length)
        {
            return HitTranslateTypes[0];
        }

        return HitTranslateTypes[index];
    }

    private static UnitNodeType NodeTypeAt(int index)
    {
        if (index < 0 || index >= NodeTypes.Length)
        {
            return NodeTypes[0];
        }

        return NodeTypes[index];
    }

    private static TimelineEventType EventTypeAt(int index)
    {
        if (index < 0 || index >= EventTypes.Length)
        {
            return EventTypes[0];
        }

        return EventTypes[index];
    }

    private static ValueType ValueTypeAt(int index)
    {
        if (index < 0 || index >= ValueTypes.Length)
        {
            return ValueTypes[0];
        }

        return ValueTypes[index];
    }

    private static TimelineBulletTrajectoryType BulletTrajectoryTypeAt(int index)
    {
        if (index < 0 || index >= BulletTrajectoryTypes.Length)
        {
            return BulletTrajectoryTypes[0];
        }

        return BulletTrajectoryTypes[index];
    }

    private static TimelineCameraShakeEnvelopeType CameraShakeEnvelopeTypeAt(int index)
    {
        if (index < 0 || index >= CameraShakeEnvelopeTypes.Length)
        {
            return CameraShakeEnvelopeTypes[0];
        }

        return CameraShakeEnvelopeTypes[index];
    }

    private static int IndexOf(TimelineCameraShotType type)
    {
        for (var i = 0; i < CameraShotTypes.Length; i++)
        {
            if (CameraShotTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(TimelineCameraShotLookAtType type)
    {
        if (type == TimelineCameraShotLookAtType.SelfOffset)
        {
            type = TimelineCameraShotLookAtType.Self;
        }

        for (var i = 0; i < CameraShotLookAtTypes.Length; i++)
        {
            if (CameraShotLookAtTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static TimelineCameraShotType CameraShotTypeAt(int index)
    {
        if (index < 0 || index >= CameraShotTypes.Length)
        {
            return CameraShotTypes[0];
        }

        return CameraShotTypes[index];
    }

    private static TimelineCameraShotLookAtType CameraShotLookAtTypeAt(int index)
    {
        if (index < 0 || index >= CameraShotLookAtTypes.Length)
        {
            return CameraShotLookAtTypes[0];
        }

        return CameraShotLookAtTypes[index];
    }
}
