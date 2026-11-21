using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class GetProjectInfoTool(ILogger<GetProjectInfoTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_project_info")]
    [Description("获取项目名、公司名、Unity 版本、平台、dataPath、激活场景与 Play/编译状态。")]
    public async Task<string> UnityGetProjectInfoAsync()
    {
        logger.LogInformation("Get project info");
        try
        {
            var result = await hub.SendRequestAsync("unity.getProjectInfo", null);
            if (result == null) return "Unity 未返回数据。";
            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) { return $"查询失败：{ex.Message}"; }
    }
}
