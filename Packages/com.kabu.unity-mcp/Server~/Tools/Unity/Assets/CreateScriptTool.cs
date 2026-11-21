using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Assets;

[McpServerToolType]
public class CreateScriptTool(ILogger<CreateScriptTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_create_script")]
    [Description("在 Assets 下创建 C# 脚本文件。contents 为空时生成同名 MonoBehaviour 模板。会触发脚本导入/编译。")]
    [return: Description("创建结果")]
    public async Task<string> UnityCreateScriptAsync(
        [Description("脚本路径，如 Assets/Scripts/PlayerController.cs")] string path,
        [Description("脚本全文；空则生成模板")] string? contents = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "请提供 path。";

        logger.LogInformation("Create script {Path}", path);
        try
        {
            var result = await hub.SendRequestAsync("unity.createScript", new
            {
                path = path.Trim().Replace('\\', '/'),
                contents
            });
            if (result == null)
                return "Unity 未返回数据。";
            if (result.Value.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "已创建脚本。";
            return result.Value.ToString();
        }
        catch (Exception ex)
        {
            return $"创建脚本失败：{ex.Message}";
        }
    }
}
