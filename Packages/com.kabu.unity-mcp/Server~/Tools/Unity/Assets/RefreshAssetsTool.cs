using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Assets;

[McpServerToolType]
public class RefreshAssetsTool(ILogger<RefreshAssetsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_refresh_assets")]
    [Description("刷新 AssetDatabase。外部改文件或写完脚本后调用，必要时会触发编译。")]
    [return: Description("刷新结果")]
    public async Task<string> UnityRefreshAssetsAsync()
    {
        logger.LogInformation("Refresh assets");
        try
        {
            var result = await hub.SendRequestAsync("unity.refreshAssets", null);
            if (result == null)
                return "已请求刷新，但 Unity 未返回数据。";

            if (result.Value.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "已刷新。";
            return "AssetDatabase 已刷新。";
        }
        catch (Exception ex)
        {
            return $"刷新失败：{ex.Message}";
        }
    }
}
