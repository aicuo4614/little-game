using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Prefabs;

[McpServerToolType]
public class ClosePrefabStageTool(ILogger<ClosePrefabStageTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_close_prefab_stage")]
    [Description("关闭 Prefab Mode，返回主场景。默认先保存；saveBeforeClosing=false 则丢弃未保存修改。")]
    [return: Description("关闭结果")]
    public async Task<string> UnityClosePrefabStageAsync(
        [Description("关闭前是否保存，默认 true")] bool saveBeforeClosing = true)
    {
        logger.LogInformation("Close prefab stage save={Save}", saveBeforeClosing);
        try
        {
            var result = await hub.SendRequestAsync("unity.closePrefabStage", new { saveBeforeClosing });
            return OpenPrefabTool.FormatMsg(result, "已关闭 Prefab Stage。");
        }
        catch (Exception ex)
        {
            return $"关闭 Prefab Stage 失败：{ex.Message}";
        }
    }
}
