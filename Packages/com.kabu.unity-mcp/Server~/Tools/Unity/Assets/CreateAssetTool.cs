using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Assets;

[McpServerToolType]
public class CreateAssetTool(ILogger<CreateAssetTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_create_asset")]
    [Description("创建 Unity 资源。assetType 支持 Material、Texture2D，或 ScriptableObject 类名。path 需含正确扩展名（.mat / .asset）。不要用此工具创建 .cs（请用 unity_create_script）。")]
    [return: Description("创建结果")]
    public async Task<string> UnityCreateAssetAsync(
        [Description("资源路径，如 Assets/Materials/Red.mat")] string path,
        [Description("类型：Material / Texture2D / ScriptableObject 类名")] string assetType,
        [Description("Material 用 Shader 名，默认 Standard")] string? shaderName = null,
        [Description("可选颜色：#RRGGBB 或 r,g,b[,a]")] string? color = null)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(assetType))
            return "请提供 path 与 assetType。";

        logger.LogInformation("Create asset {Type} at {Path}", assetType, path);
        try
        {
            var result = await hub.SendRequestAsync("unity.createAsset", new
            {
                path = path.Trim().Replace('\\', '/'),
                assetType = assetType.Trim(),
                shaderName = string.IsNullOrWhiteSpace(shaderName) ? null : shaderName.Trim(),
                color = string.IsNullOrWhiteSpace(color) ? null : color.Trim()
            });
            if (result == null)
                return "Unity 未返回数据。";
            if (result.Value.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "已创建资源。";
            return result.Value.ToString();
        }
        catch (Exception ex)
        {
            return $"创建资源失败：{ex.Message}";
        }
    }
}
