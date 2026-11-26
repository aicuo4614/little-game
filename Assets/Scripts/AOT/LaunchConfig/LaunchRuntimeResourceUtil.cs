/// <summary>
/// Editor 是否走真机同款 YooAsset 管线（Offline / HostPlayMode），见 <see cref="LaunchSOConfig.EditorSimulateDevice"/>。
/// </summary>
public static class LaunchRuntimeResourceUtil
{
    /// <summary>
    /// 真机恒为 true；Editor 仅当 <see cref="LaunchSOConfig.EditorSimulateDevice"/> 勾选时为 true。
    /// 为 true 时不使用 EditorSimulateMode，不从 Project 磁盘直读 FairyGUI 包；Editor 热更代码仍用 asmdef 直编（不从 CDN 加载 DLL）。
    /// </summary>
    public static bool UseRuntimeResourcePipeline
    {
        get
        {
#if UNITY_EDITOR
            var cfg = MyApplication.GetLaunchSOConfig();
            return cfg != null && cfg.EditorSimulateDevice;
#else
            return true;
#endif
        }
    }
}
