using System;
using System.Globalization;

/// <summary>预览对象只使用相对施法者的角色键。</summary>
public static class TimelinePreviewActorKey
{
    /// <summary>验证角色键语法；实际对象由预览单位列表解析，不把不存在的目标替换为自己。</summary>
    public static void Validate(string key)
    {
        var value = key ?? string.Empty;
        if (value == "$caster")
        {
            return;
        }
        var prefix = value.StartsWith("$ally:", StringComparison.Ordinal) ? "$ally:"
            : value.StartsWith("$enemy:", StringComparison.Ordinal) ? "$enemy:" : string.Empty;
        if (prefix.Length > 0 && int.TryParse(value.Substring(prefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var index) && index >= 0)
        {
            return;
        }
        throw new ArgumentException("Invalid preview actor key: " + key + ". Use $caster, $ally:n or $enemy:n.");
    }
}
