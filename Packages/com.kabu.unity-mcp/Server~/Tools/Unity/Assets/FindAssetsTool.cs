using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Assets;

[McpServerToolType]
public class FindAssetsTool(ILogger<FindAssetsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_find_assets")]
    [Description("搜索项目资源。filter 支持名称、t:类型、l:标签等（同 AssetDatabase.FindAssets）。可选限制文件夹与返回数量。")]
    [return: Description("匹配资源列表")]
    public async Task<string> UnityFindAssetsAsync(
        [Description("搜索过滤，如 Player 或 t:Prefab 或 t:Scene Sample")] string? filter = null,
        [Description("限制搜索文件夹，如 Assets/Prefabs；空=全项目")] string? folder = null,
        [Description("最多返回条数，默认 20")] int maxResults = 20)
    {
        logger.LogInformation("Find assets filter={Filter}", filter);
        try
        {
            var result = await hub.SendRequestAsync("unity.findAssets", new
            {
                filter = filter ?? string.Empty,
                folder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim().Replace('\\', '/'),
                maxResults
            });
            if (result == null)
                return "Unity 未返回数据。";

            var el = result.Value;
            if (!el.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return el.ToString();

            var sb = new StringBuilder();
            var count = el.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
            var truncated = el.TryGetProperty("truncated", out var t) && t.GetBoolean();
            sb.AppendLine($"找到 {count} 个资源{(truncated ? "（已截断）" : "")}：");
            foreach (var a in assets.EnumerateArray())
            {
                var path = a.TryGetProperty("path", out var p) ? p.GetString() : "?";
                var type = a.TryGetProperty("typeName", out var ty) ? ty.GetString() : "?";
                sb.AppendLine($"- [{type}] {path}");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"搜索资源失败：{ex.Message}";
        }
    }
}
