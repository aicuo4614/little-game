using System.ComponentModel;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Components;

[McpServerToolType]
public class SetComponentFieldTool(ILogger<SetComponentFieldTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_set_component_field")]
    [Description("设置组件公共字段或属性。valueType 可选：auto/int/float/bool/vector2/vector3/color/asset/gameobject/string。vector 用逗号分隔，如 1,2,3；color 可用 #RRGGBB 或 r,g,b[,a]；asset 传 Assets 路径。")]
    [return: Description("设置结果")]
    public async Task<string> UnitySetComponentFieldAsync(
        [Description("组件类型名")] string componentType,
        [Description("字段或属性名")] string fieldName,
        [Description("要写入的值（字符串形式）")] string value,
        [Description("值类型提示，默认 auto")] string valueType = "auto",
        [Description("物体 instanceId")] int instanceId = 0,
        [Description("物体层级路径")] string? path = null,
        [Description("物体名称")] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(componentType) || string.IsNullOrWhiteSpace(fieldName))
            return "请提供 componentType 与 fieldName。";

        logger.LogInformation("Set {Type}.{Field}", componentType, fieldName);
        try
        {
            var result = await hub.SendRequestAsync("unity.setComponentField", new
            {
                componentType = componentType.Trim(),
                fieldName = fieldName.Trim(),
                value,
                valueType = string.IsNullOrWhiteSpace(valueType) ? "auto" : valueType.Trim(),
                instanceId = instanceId == 0 ? (int?)null : instanceId,
                path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim()
            });
            return AddComponentTool.FormatMessage(result, "已设置字段。");
        }
        catch (Exception ex)
        {
            return $"设置字段失败：{ex.Message}";
        }
    }
}
