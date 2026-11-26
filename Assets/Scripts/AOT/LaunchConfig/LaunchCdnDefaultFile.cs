using System;

/// <summary>
/// <c>cdn.default.json</c>：单个 CDN 根地址。没有合法覆盖文件时使用。
/// </summary>
[Serializable]
public class LaunchCdnDefaultFile
{
    /// <summary>CDN 根地址，例如 http://192.168.233.75:91。</summary>
    public string url;

    /// <summary>
    /// <see cref="url"/> 是 http/https 时返回去掉首尾空白的地址。
    /// </summary>
    /// <param name="selected">合法时的根地址。</param>
    /// <returns>地址可用时为 true。</returns>
    public bool TryGetUrl(out string selected)
    {
        selected = string.Empty;
        if (!IsHttpUrl(url))
        {
            return false;
        }

        selected = url.Trim();
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
