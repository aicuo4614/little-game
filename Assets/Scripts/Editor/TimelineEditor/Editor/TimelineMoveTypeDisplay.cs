using UnityEditor;

/// <summary>通用移动 / 跟随相关枚举在编辑器中的中文显示名。</summary>
public static class TimelineMoveTypeDisplay
{
    private static readonly TimelineMoveEntityType[] EntityTypes =
    {
        TimelineMoveEntityType.Camera,
        TimelineMoveEntityType.Effect,
    };

    private static readonly TranslateMovePointType[] TranslateTypes =
    {
        TranslateMovePointType.TranslateToSelf,
        TranslateMovePointType.TranslateToTarget,
        TranslateMovePointType.TranslateToSelfTargetCenter,
        TranslateMovePointType.None,
    };

    private static readonly RotateMovePointType[] RotateTypes =
    {
        RotateMovePointType.RotateToSelf,
        RotateMovePointType.RotateToTarget,
        RotateMovePointType.RotateToSelfTargetCenter,
        RotateMovePointType.None,
    };

    private static string[] entityLabels;
    private static string[] translateLabels;
    private static string[] rotateLabels;

    public static string GetDisplayName(TimelineMoveEntityType type)
    {
        switch (type)
        {
            case TimelineMoveEntityType.Camera:
                return "相机";
            case TimelineMoveEntityType.Effect:
                return "特效";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(TranslateMovePointType type)
    {
        switch (type)
        {
            case TranslateMovePointType.TranslateToSelf:
                return "移向自身";
            case TranslateMovePointType.TranslateToTarget:
                return "移向目标";
            case TranslateMovePointType.TranslateToSelfTargetCenter:
                return "移向自身与目标中点";
            case TranslateMovePointType.None:
                return "无";
            default:
                return type.ToString();
        }
    }

    public static string GetDisplayName(RotateMovePointType type)
    {
        switch (type)
        {
            case RotateMovePointType.RotateToSelf:
                return "朝向自身";
            case RotateMovePointType.RotateToTarget:
                return "朝向目标";
            case RotateMovePointType.RotateToSelfTargetCenter:
                return "朝向自身与目标中点";
            case RotateMovePointType.None:
                return "无";
            default:
                return type.ToString();
        }
    }

    public static TimelineMoveEntityType DrawEntityPopup(string label, TimelineMoveEntityType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetEntityLabels());
        return EntityTypeAt(index);
    }

    public static TranslateMovePointType DrawTranslatePopup(string label, TranslateMovePointType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetTranslateLabels());
        return TranslateTypeAt(index);
    }

    public static RotateMovePointType DrawRotatePopup(string label, RotateMovePointType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetRotateLabels());
        return RotateTypeAt(index);
    }

    private static string[] GetEntityLabels()
    {
        if (entityLabels != null && entityLabels.Length == EntityTypes.Length)
        {
            return entityLabels;
        }

        entityLabels = new string[EntityTypes.Length];
        for (var i = 0; i < EntityTypes.Length; i++)
        {
            entityLabels[i] = GetDisplayName(EntityTypes[i]);
        }

        return entityLabels;
    }

    private static string[] GetTranslateLabels()
    {
        if (translateLabels != null && translateLabels.Length == TranslateTypes.Length)
        {
            return translateLabels;
        }

        translateLabels = new string[TranslateTypes.Length];
        for (var i = 0; i < TranslateTypes.Length; i++)
        {
            translateLabels[i] = GetDisplayName(TranslateTypes[i]);
        }

        return translateLabels;
    }

    private static string[] GetRotateLabels()
    {
        if (rotateLabels != null && rotateLabels.Length == RotateTypes.Length)
        {
            return rotateLabels;
        }

        rotateLabels = new string[RotateTypes.Length];
        for (var i = 0; i < RotateTypes.Length; i++)
        {
            rotateLabels[i] = GetDisplayName(RotateTypes[i]);
        }

        return rotateLabels;
    }

    private static int IndexOf(TimelineMoveEntityType type)
    {
        for (var i = 0; i < EntityTypes.Length; i++)
        {
            if (EntityTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(TranslateMovePointType type)
    {
        for (var i = 0; i < TranslateTypes.Length; i++)
        {
            if (TranslateTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(RotateMovePointType type)
    {
        for (var i = 0; i < RotateTypes.Length; i++)
        {
            if (RotateTypes[i] == type)
            {
                return i;
            }
        }

        return 0;
    }

    private static TimelineMoveEntityType EntityTypeAt(int index)
    {
        if (index < 0 || index >= EntityTypes.Length)
        {
            return EntityTypes[0];
        }

        return EntityTypes[index];
    }

    private static TranslateMovePointType TranslateTypeAt(int index)
    {
        if (index < 0 || index >= TranslateTypes.Length)
        {
            return TranslateTypes[0];
        }

        return TranslateTypes[index];
    }

    private static RotateMovePointType RotateTypeAt(int index)
    {
        if (index < 0 || index >= RotateTypes.Length)
        {
            return RotateTypes[0];
        }

        return RotateTypes[index];
    }
}
