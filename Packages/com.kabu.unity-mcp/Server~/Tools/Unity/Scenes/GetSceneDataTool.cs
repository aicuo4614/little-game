using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Scenes;

[McpServerToolType]
public class GetSceneDataTool(ILogger<GetSceneDataTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_scene_data")]
    [Description("获取指定（或当前激活）已打开场景的摘要与根物体层级。只读。")]
    [return: Description("场景数据文本")]
    public async Task<string> UnityGetSceneDataAsync(
        [Description("已打开场景名称；空=激活场景")] string? sceneName = null,
        [Description("是否包含根物体层级，默认 true")] bool includeRoots = true,
        [Description("层级深度，0=仅根，默认 2")] int hierarchyDepth = 2)
    {
        logger.LogInformation("Get scene data {Name}", sceneName);
        try
        {
            var result = await hub.SendRequestAsync("unity.getSceneData", new
            {
                sceneName = string.IsNullOrWhiteSpace(sceneName) ? null : sceneName.Trim(),
                includeRoots,
                hierarchyDepth
            });
            if (result == null)
                return "Unity 未返回数据。";

            var el = result.Value;
            var sb = new StringBuilder();
            if (el.TryGetProperty("scene", out var scene))
            {
                sb.AppendLine($"场景：{Prop(scene, "name")}（{Prop(scene, "path")}）");
                if (scene.TryGetProperty("isDirty", out var dirty) && dirty.GetBoolean())
                    sb.AppendLine("状态：已修改未保存");
                if (scene.TryGetProperty("rootCount", out var rc))
                    sb.AppendLine($"根物体数：{rc.GetInt32()}");
            }

            if (el.TryGetProperty("objects", out var objects) && objects.ValueKind == JsonValueKind.Array)
            {
                sb.AppendLine("层级：");
                foreach (var obj in objects.EnumerateArray())
                {
                    var depth = obj.TryGetProperty("depth", out var d) ? d.GetInt32() : 0;
                    var name = Prop(obj, "name");
                    var id = obj.TryGetProperty("instanceId", out var idEl) ? idEl.GetInt32() : 0;
                    var active = !obj.TryGetProperty("active", out var a) || a.GetBoolean();
                    sb.AppendLine($"{new string(' ', depth * 2)}- {name}  id={id}{(active ? "" : " (inactive)")}");
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"获取场景数据失败：{ex.Message}";
        }
    }

    private static string Prop(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) ? v.ToString().Trim('"') : "?";
}
