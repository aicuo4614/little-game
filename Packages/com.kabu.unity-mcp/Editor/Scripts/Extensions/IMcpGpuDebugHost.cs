namespace KabuUnityMcp.Editor.Extensions
{
    /// <summary>
    /// 工程侧 GPU 调试 Host：把业务缓冲注册进 <c>McpGpuBufferRegistry</c>，并提供 CPU 镜像读取。
    /// 通用包不引用具体玩法类型（如 HeroPartBatchPool）。
    /// </summary>
    public interface IMcpGpuDebugHost
    {
        /// <summary>扫描并刷新注册表；返回已注册条数等摘要。</summary>
        McpHostResult Refresh();

        /// <summary>
        /// 读取 CPU 侧镜像切片（不触发 GPU readback）。
        /// bufferId 须与注册表一致。
        /// </summary>
        McpHostResult ReadCpuMirror(string bufferId, int offset, int limit);
    }
}
