using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Assets;

[McpServerToolType]
public class MoveAssetTool(ILogger<MoveAssetTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_move_asset")]
    [Description("移动或重命名项目内资源。目标父目录不存在时会自动创建。")]
    [return: Description("移动结果")]
    public async Task<string> UnityMoveAssetAsync(
        [Description("源路径")] string sourcePath,
        [Description("目标路径")] string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            return "请提供 sourcePath 与 destinationPath。";

        logger.LogInformation("Move asset {Src} -> {Dst}", sourcePath, destinationPath);
        try
        {
            var result = await hub.SendRequestAsync("unity.moveAsset", new
            {
                sourcePath = sourcePath.Trim().Replace('\\', '/'),
                destinationPath = destinationPath.Trim().Replace('\\', '/')
            });
            if (result == null)
                return "已请求移动，但 Unity 未返回数据。";
            if (result.Value.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "已移动。";
            return result.Value.ToString();
        }
        catch (Exception ex)
        {
            return $"移动失败：{ex.Message}";
        }
    }
}
