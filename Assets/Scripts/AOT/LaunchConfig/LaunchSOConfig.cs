using UnityEngine;

/// <summary>
/// 启动配置（Resources 内置，不依赖 YooAsset / ResourceMgr）。
/// 加载路径：<c>Resources.Load("LaunchConfig/LaunchSOConfig")</c>。
/// CDN 根地址在同目录的 <c>cdn.default.json</c>；<c>cdn.json</c> 存在且合法时覆盖。
/// </summary>
[CreateAssetMenu(fileName = "LaunchSOConfig", menuName = "Kabu/LaunchSOConfig")]
public class LaunchSOConfig : ScriptableObject
{
    /// <summary>是否输出日志（含全部 <c>Debug.Log*</c>）。关闭后 Exception 仍会输出。</summary>
    public bool LogEnabled = true;

    /// <summary>
    /// 是否显示开屏 <c>SplashPanel</c>。默认不勾选，直接进入 <c>LauncherPanel</c>。
    /// </summary>
    public bool ShowSplash;

    /// <summary>
    /// 为 true 时：连接游戏服使用调试登录面板填写的服务器地址（host:port），
    /// 忽略账号服下发的 <c>TargetGS</c>。用于 USB adb reverse / 本机调试。
    /// </summary>
    public bool UseLoginServerAsGameServer;

    /// <summary>
    /// 仅 Editor Play：勾选后走真机同款 YooAsset 资源管线（LaunchPackage=StreamingAssets 离线包，其余=HostPlayMode+CDN），
    /// 热更 DLL 仍用 Editor asmdef 直编，避免双程序集与 AB 脚本类型冲突。
    /// 不再 EditorSimulateMode、不从 <c>Assets/Package_Launch</c> 磁盘直读 FairyGUI。
    /// 需 CDN JSON 指向已启动的本地 HTTP，且 LaunchPackage 已打入 StreamingAssets。
    /// </summary>
    public bool EditorSimulateDevice;

    /// <summary>Resources 相对路径（无扩展名）。</summary>
    public const string ResourcesPath = "LaunchConfig/LaunchSOConfig";
}
