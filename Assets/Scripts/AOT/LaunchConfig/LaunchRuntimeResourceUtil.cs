/// <summary>
/// Editor 是否从构建后的资源包读取资源，见 <see cref="LaunchSOConfig.EditorSimulateDevice"/>。
/// </summary>
public static class LaunchRuntimeResourceUtil
{
    /// <summary>
    /// 真机恒为 true；Editor 仅当 <see cref="LaunchSOConfig.EditorSimulateDevice"/> 勾选时为 true。
    /// Editor 的代码始终用 asmdef 直编，资源可切换到真机同款内置包。
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
