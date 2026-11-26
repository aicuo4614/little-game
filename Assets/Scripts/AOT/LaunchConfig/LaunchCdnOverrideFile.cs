using System;

/// <summary>
/// <c>cdn.json</c>：CDN 根地址列表，以及当前选用的下标。内容合法时覆盖 default。
/// </summary>
[Serializable]
public class LaunchCdnOverrideFile
{
    /// <summary>CDN 根地址列表。每项为 http 或 https 根地址。</summary>
    public string[] urls;

    /// <summary>选用 <see cref="urls"/> 的下标，从 0 开始。越界则整份覆盖无效。</summary>
    public int index;

    /// <summary>
    /// 下标在 <see cref="urls"/> 范围内，且选中项是 http/https 时，返回该根地址。
    /// </summary>
    /// <param name="selected">合法时的根地址，已去掉首尾空白。</param>
    /// <returns>选中项可用时为 true。</returns>
    public bool TryGetSelectedUrl(out string selected)
    {
        selected = string.Empty;
        if (urls == null || index < 0 || index >= urls.Length)
        {
            return false;
        }

        if (!IsHttpUrl(urls[index]))
        {
            return false;
        }

        selected = urls[index].Trim();
        return true;
    }

    private static bool IsHttpUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
