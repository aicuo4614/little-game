using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Assets;

[McpServerToolType]
public class DeleteAssetTool(ILogger<DeleteAssetTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_delete_asset")]
    [Description("删除项目内资源（含文件夹）。破坏性操作，请先用 find 确认路径。")]
    [return: Description("删除结果")]
    public async Task<string> UnityDeleteAssetAsync(
        [Description("要删除的资源路径")] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "请提供 path。";

        logger.LogInformation("Delete asset {Path}", path);
        try
        {
            var result = await hub.SendRequestAsync("unity.deleteAsset", new
            {
                path = path.Trim().Replace('\\', '/')
            });
            if (result == null)
                return "已请求删除，但 Unity 未返回数据。";
            if (result.Value.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "已删除。";
            return result.Value.ToString();
        }
        catch (Exception ex)
        {
            return $"删除失败：{ex.Message}";
        }
    }
}
