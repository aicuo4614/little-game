using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Prefabs;

[McpServerToolType]
public class OpenPrefabTool(ILogger<OpenPrefabTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_open_prefab")]
    [Description("在 Prefab Mode（隔离模式）中打开 Prefab 资源进行编辑。")]
    [return: Description("打开结果")]
    public async Task<string> UnityOpenPrefabAsync(
        [Description("Prefab 路径，如 Assets/Prefabs/Enemy.prefab")] string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
            return "请提供 prefabPath。";

        logger.LogInformation("Open prefab {Path}", prefabPath);
        try
        {
            var result = await hub.SendRequestAsync("unity.openPrefab", new
            {
                prefabPath = prefabPath.Trim().Replace('\\', '/')
            });
            return FormatMsg(result, "已打开 Prefab Mode。");
        }
        catch (Exception ex)
        {
            return $"打开 Prefab 失败：{ex.Message}";
        }
    }

    internal static string FormatMsg(System.Text.Json.JsonElement? result, string fallback)
    {
        if (result == null) return "Unity 未返回数据。";
        if (result.Value.TryGetProperty("message", out var msg))
            return msg.GetString() ?? fallback;
        return result.Value.ToString();
    }
}
