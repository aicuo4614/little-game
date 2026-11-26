using UnityEditor;

/// <summary>PointOffsetValueType 在编辑器中的中文显示名。</summary>
public static class PointOffsetValueTypeDisplay
{
    private static readonly PointOffsetValueType[] AllTypes =
    {
        PointOffsetValueType.UnitLocal,
        PointOffsetValueType.World,
        PointOffsetValueType.ArcFromChord,
    };

    private static string[] typeLabels;

    public static string GetDisplayName(PointOffsetValueType type)
    {
        switch (type)
        {
            case PointOffsetValueType.UnitLocal:
                return "朝向自身";
            case PointOffsetValueType.World:
                return "世界";
            case PointOffsetValueType.ArcFromChord:
                return "自身朝向目标点";
            default:
                return type.ToString();
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

    public static int IndexOf(PointOffsetValueType type)
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

    public static PointOffsetValueType TypeAt(int index)
    {
        if (index < 0 || index >= AllTypes.Length)
        {
            return AllTypes[0];
        }

        return AllTypes[index];
    }

    public static PointOffsetValueType DrawPopup(string label, PointOffsetValueType value)
    {
        var index = EditorGUILayout.Popup(label, IndexOf(value), GetTypeLabels());
        return TypeAt(index);
    }
}
