using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.SystemTools;

[McpServerToolType]
public class GetCompilationStatusTool(ILogger<GetCompilationStatusTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_get_compilation_status")]
    [Description("查询脚本是否正在编译，以及最近一次编译是否成功。")]
    public async Task<string> UnityGetCompilationStatusAsync()
    {
        logger.LogInformation("Get compilation status");
        try
        {
            var result = await hub.SendRequestAsync("unity.getCompilationStatus", null);
            if (result?.TryGetProperty("message", out var msg) == true)
                return msg.GetString() ?? result.Value.ToString();
            return result?.ToString() ?? "Unity 未返回数据。";
        }
        catch (Exception ex) { return $"查询失败：{ex.Message}"; }
    }
}
