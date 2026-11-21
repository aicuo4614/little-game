using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.GameObjects;

[McpServerToolType]
public class BatchCreateGameObjectsTool(ILogger<BatchCreateGameObjectsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_batch_create_game_objects")]
    [Description("在当前激活场景批量创建多个 GameObject。gameObjectsJson 为 JSON 数组，每项含 name，可选 position/rotation/scale/parent/primitiveType 等。可用 Ctrl+Z 逐项撤销。")]
    public async Task<string> UnityBatchCreateGameObjectsAsync(
        [Description("JSON 数组，例如：[{\"name\":\"A\",\"position\":{\"x\":0,\"y\":1,\"z\":0}},{\"name\":\"B\",\"primitiveType\":\"Cube\"}]")]
        string gameObjectsJson)
    {
        if (string.IsNullOrWhiteSpace(gameObjectsJson))
            return "请提供 gameObjectsJson（JSON 数组）。";

        logger.LogInformation("Batch create game objects");
        try
        {
            using var doc = JsonDocument.Parse(gameObjectsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return "gameObjectsJson 必须是 JSON 数组。";

            var result = await hub.SendRequestAsync("unity.batchCreateGameObjects", new
            {
                gameObjectsJson = gameObjectsJson.Trim()
            });

            if (result == null)
                return "Unity 未返回数据。";

            var sb = new StringBuilder();
            if (result.Value.TryGetProperty("message", out var msg))
                sb.AppendLine(msg.GetString());
            if (result.Value.TryGetProperty("createdCount", out var cc))
                sb.AppendLine($"成功: {cc.GetInt32()}");
            if (result.Value.TryGetProperty("errorCount", out var ec))
                sb.AppendLine($"失败: {ec.GetInt32()}");
            if (result.Value.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                sb.AppendLine("错误详情:");
                foreach (var e in errors.EnumerateArray())
                    sb.AppendLine($"  - {e}");
            }

            var text = sb.ToString().Trim();
            return string.IsNullOrEmpty(text) ? result.Value.ToString() : text;
        }
        catch (JsonException)
        {
            return "gameObjectsJson 不是合法 JSON。";
        }
        catch (Exception ex)
        {
            return $"批量创建失败：{ex.Message}";
        }
    }
}
