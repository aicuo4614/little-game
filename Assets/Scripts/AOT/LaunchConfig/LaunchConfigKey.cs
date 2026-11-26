/// <summary>
/// 启动相关的端口约定。CDN 根地址在 <c>cdn.default.json</c> / <c>cdn.json</c>。
/// </summary>
public static class LaunchConfigKey
{
    /// <summary>调试登录游戏服端口。服务器只填地址，端口单独一项，默认 7008。</summary>
    public const int LoginServerPort = 7008;

    /// <summary>远程配置服务端口。勾选 Config dev 时拼成 http://{IP}:8080。</summary>
    public const int RemoteConfigPort = 8080;

    /// <summary>CDN JSON 都不可用时，<see cref="ResourceMgr"/> 使用的本地兜底端口。</summary>
    public const int CdnPort = 8082;
}
