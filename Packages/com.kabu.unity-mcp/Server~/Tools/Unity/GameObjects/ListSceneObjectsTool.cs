using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.GameObjects;

[McpServerToolType]
public class ListSceneObjectsTool(ILogger<ListSceneObjectsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_list_scene_objects")]
    [Description("列出当前激活场景中的 GameObject 层级（只读），含 path 与 instanceId。需要 Unity 已通过 WebSocket 连接。")]
    [return: Description("场景物体层级文本")]
    public async Task<string> UnityListSceneObjectsAsync(
        [Description("是否包含未激活物体，默认 true")] bool includeInactive = true)
    {
        logger.LogInformation("Listing scene objects");
        try
        {
            var result = await hub.SendRequestAsync("unity.listSceneObjects", new { includeInactive });
            if (result == null)
                return "Unity 未返回数据。";

            return FormatHierarchy(result.Value);
        }
        catch (Exception ex)
        {
            return $"列出场景物体失败：{ex.Message}";
        }
    }

    private static string FormatHierarchy(JsonElement result)
    {
        var sceneName = result.TryGetProperty("sceneName", out var sn) ? sn.GetString() : "?";
        var scenePath = result.TryGetProperty("scenePath", out var sp) ? sp.GetString() : "";
        if (!result.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array)
            return result.ToString();

        var sb = new StringBuilder();
        sb.AppendLine($"场景：{sceneName}（{scenePath}）");
        var count = 0;
        foreach (var obj in objects.EnumerateArray())
        {
            count++;
            var depth = obj.TryGetProperty("depth", out var d) ? d.GetInt32() : 0;
            var name = obj.TryGetProperty("name", out var n) ? n.GetString() : "?";
            var id = obj.TryGetProperty("instanceId", out var idEl) ? idEl.GetInt32() : 0;
            var active = !obj.TryGetProperty("active", out var a) || a.GetBoolean();
            var indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}- {name}  id={id}{(active ? "" : " (inactive)")}");
        }

        if (count == 0)
            sb.AppendLine("（空场景，没有根物体）");
        else
            sb.AppendLine($"共 {count} 个物体");

        return sb.ToString().TrimEnd();
    }
}
