using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Profiler;

[McpServerToolType]
public class GpuReadBufferTool(ILogger<GpuReadBufferTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_gpu_read_buffer")]
    [Description(
        "读取已注册 GraphicsBuffer 的切片摘要（schema=kabu.gpu.buffer-read/v1）：items + min/max/NaN/md5。" +
        "source=gpu（默认）走 AsyncGPUReadback，需等编辑器帧，有 GPU→CPU 开销；source=cpu 读工程 Host 提供的镜像（零 GPU 往返）。" +
        "offset/limit 分页，limit 默认 32、最大 256。禁止一次读整块蒙皮 VB。" +
        "须先 unity_gpu_list_buffers 拿到 bufferId。Play Mode + 缓冲仍存活时最稳。")]
    public async Task<string> UnityGpuReadBufferAsync(
        [Description("缓冲 id（来自 list）")] string bufferId,
        [Description("元素起始偏移，默认 0")] int offset = 0,
        [Description("返回元素数，默认 32，最大 256")] int limit = 32,
        [Description("gpu=AsyncGPUReadback；cpu=Host CPU 镜像")] string source = "gpu")
    {
        logger.LogInformation("gpu read buffer {Id} source={Source}", bufferId, source);
        try
        {
            var result = await hub.SendRequestAsync(
                "unity.gpuReadBuffer",
                new
                {
                    bufferId,
                    offset,
                    limit,
                    source = string.IsNullOrWhiteSpace(source) ? "gpu" : source.Trim()
                },
                timeoutSeconds: 60);
            if (result == null) return "Unity 未返回数据。";
            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"读取 GPU buffer 失败：{ex.Message}";
        }
    }
}
