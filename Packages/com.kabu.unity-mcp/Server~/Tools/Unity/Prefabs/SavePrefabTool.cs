using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Prefabs;

[McpServerToolType]
public class SavePrefabTool(ILogger<SavePrefabTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_save_prefab")]
    [Description("保存当前 Prefab Stage；若传 prefabPath，则将场景中该 Prefab 实例的覆盖 Apply 回资源。")]
    [return: Description("保存结果")]
    public async Task<string> UnitySavePrefabAsync(
        [Description("可选。指定 Prefab 路径则应用场景实例覆盖；空则保存当前 Stage")] string? prefabPath = null)
    {
        logger.LogInformation("Save prefab {Path}", prefabPath);
        try
        {
            var result = await hub.SendRequestAsync("unity.savePrefab", new
            {
                prefabPath = string.IsNullOrWhiteSpace(prefabPath)
                    ? null
                    : prefabPath.Trim().Replace('\\', '/')
            });
            return OpenPrefabTool.FormatMsg(result, "已保存 Prefab。");
        }
        catch (Exception ex)
        {
            return $"保存 Prefab 失败：{ex.Message}";
        }
    }
}
