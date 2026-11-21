using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class GpuListBuffersTool(ILogger<GpuListBuffersTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_gpu_list_buffers")]
    [Description(
        "分页列出已注册的 GraphicsBuffer（schema=kabu.gpu.buffer-list/v1）：id/name/ownerPath/schema/stride/count/target/approxBytes/hasCpuMirror。" +
        "默认 refreshHost=true：若工程注册了 IMcpGpuDebugHost 会先刷新注册表（如 HeroPartBatch）。" +
        "不含缓冲内容——用 unity_gpu_read_buffer。无法读取未注册/无 C# 引用的 GPU 内存。")]
    public async Task<string> UnityGpuListBuffersAsync(
        [Description("起始偏移，默认 0")] int offset = 0,
        [Description("返回条数，默认 32，最大 200")] int limit = 32,
        [Description("按 id/name/ownerPath 子串过滤")] string? nameContains = null,
        [Description("是否先调用工程 GpuDebug Host.Refresh，默认 true")] bool refreshHost = true)
    {
        logger.LogInformation("gpu list buffers offset={Offset} limit={Limit}", offset, limit);
        try
        {
            var result = await hub.SendRequestAsync("unity.gpuListBuffers", new
            {
                offset,
                limit,
                nameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains.Trim(),
                refreshHost
            });
            if (result == null) return "Unity 未返回数据。";
            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"列出 GPU buffers 失败：{ex.Message}";
        }
    }
}
