using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Assets;

[McpServerToolType]
public class CreateFolderTool(ILogger<CreateFolderTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_create_folder")]
    [Description("在已存在的父文件夹下创建子文件夹。parentPath 须以 Assets 开头且已存在。")]
    [return: Description("创建结果")]
    public async Task<string> UnityCreateFolderAsync(
        [Description("父文件夹路径，如 Assets 或 Assets/Scripts")] string parentPath,
        [Description("新文件夹名（不含斜杠）")] string folderName)
    {
        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(folderName))
            return "请提供 parentPath 与 folderName。";

        logger.LogInformation("Create folder {Parent}/{Name}", parentPath, folderName);
        try
        {
            var result = await hub.SendRequestAsync("unity.createFolder", new
            {
                parentPath = parentPath.Trim().Replace('\\', '/'),
                folderName = folderName.Trim()
            });
            if (result == null)
                return "已请求创建，但 Unity 未返回数据。";

            if (result.Value.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "已创建。";
            return result.Value.ToString();
        }
        catch (Exception ex)
        {
            return $"创建文件夹失败：{ex.Message}";
        }
    }
}
