/// <summary>
/// <see cref="ResourceService"/> 缓存条目只读快照（Debug 用）。
/// </summary>
public sealed class ResourceEntrySnapshot
{
    /// <summary>内部键：PackageName|path。</summary>
    public string Key;

    /// <summary>资源包名字符串（与 <see cref="ResourcePackageName"/> 一致）。</summary>
    public string PackageName;

    /// <summary>YooAsset 地址。</summary>
    public string Path;

    /// <summary>当前引用计数。</summary>
    public int RefCount;

    /// <summary>Handle 是否仍有效。</summary>
    public bool HandleValid;
}
