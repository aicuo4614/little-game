using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Assets;

[McpServerToolType]
public class CopyAssetTool(ILogger<CopyAssetTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_copy_asset")]
    [Description("复制项目内资源到新路径。目标父目录不存在时会自动创建。")]
    [return: Description("复制结果")]
    public async Task<string> UnityCopyAssetAsync(
        [Description("源资源路径，如 Assets/A.prefab")] string sourcePath,
        [Description("目标路径，如 Assets/B.prefab")] string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            return "请提供 sourcePath 与 destinationPath。";

        logger.LogInformation("Copy asset {Src} -> {Dst}", sourcePath, destinationPath);
        try
        {
            var result = await hub.SendRequestAsync("unity.copyAsset", new
            {
                sourcePath = sourcePath.Trim().Replace('\\', '/'),
                destinationPath = destinationPath.Trim().Replace('\\', '/')
            });
            if (result == null)
                return "已请求复制，但 Unity 未返回数据。";
            if (result.Value.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "已复制。";
            return result.Value.ToString();
        }
        catch (Exception ex)
        {
            return $"复制失败：{ex.Message}";
        }
    }
}
