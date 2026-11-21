using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Scenes;

[McpServerToolType]
public class ListOpenedScenesTool(ILogger<ListOpenedScenesTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_list_opened_scenes")]
    [Description("列出编辑器中当前已打开的所有场景（名称、路径、是否激活/脏）。只读。")]
    [return: Description("已打开场景列表")]
    public async Task<string> UnityListOpenedScenesAsync()
    {
        logger.LogInformation("List opened scenes");
        try
        {
            var result = await hub.SendRequestAsync("unity.listOpenedScenes", null);
            if (result == null)
                return "Unity 未返回数据。";

            if (!result.Value.TryGetProperty("scenes", out var scenes) || scenes.ValueKind != JsonValueKind.Array)
                return result.Value.ToString();

            var sb = new StringBuilder();
            sb.AppendLine("已打开场景：");
            foreach (var s in scenes.EnumerateArray())
            {
                var name = s.TryGetProperty("name", out var n) ? n.GetString() : "?";
                var path = s.TryGetProperty("path", out var p) ? p.GetString() : "";
                var active = s.TryGetProperty("isActive", out var a) && a.GetBoolean();
                var dirty = s.TryGetProperty("isDirty", out var d) && d.GetBoolean();
                var roots = s.TryGetProperty("rootCount", out var r) ? r.GetInt32() : 0;
                sb.AppendLine($"- {name}{(active ? " [激活]" : "")}{(dirty ? " *" : "")}  roots={roots}  {path}");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"列出场景失败：{ex.Message}";
        }
    }
}
