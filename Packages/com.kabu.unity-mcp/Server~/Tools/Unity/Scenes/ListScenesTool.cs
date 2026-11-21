using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Scenes;

[McpServerToolType]
public class ListScenesTool(ILogger<ListScenesTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_list_scenes")]
    [Description("列出项目 Assets 内全部场景资源（.unity）路径，与是否已打开无关。")]
    public async Task<string> UnityListScenesAsync(
        [Description("最多返回条数，默认 200")] int maxResults = 200)
    {
        logger.LogInformation("List project scenes");
        try
        {
            var result = await hub.SendRequestAsync("unity.listScenes", new { maxResults });
            if (result == null) return "Unity 未返回数据。";
            if (result.Value.TryGetProperty("scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"共 {scenes.GetArrayLength()} 个场景：");
                foreach (var s in scenes.EnumerateArray())
                {
                    var path = s.TryGetProperty("path", out var p) ? p.GetString() : s.ToString();
                    sb.AppendLine("- " + path);
                }
                return sb.ToString();
            }
            return result.Value.ToString();
        }
        catch (Exception ex) { return $"列出场景失败：{ex.Message}"; }
    }
}
