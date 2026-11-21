using System.ComponentModel;
using System.Text.Json;
using KabuUnityMcp.Server.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KabuUnityMcp.Server.Tools.Unity.Diagnostics;

[McpServerToolType]
public sealed class DiagnosticTools(UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_extension_catalog", ReadOnly = true)]
    [Description("查询当前 Editor 的可选集成和项目扩展：版本、可用状态、操作、参数 schema；未安装的集成给出禁用原因。不加载场景或创建 UI。")]
    public Task<CallToolResult> Catalog(string? extensionId = null) => Send("unity.extensionCatalog", new { extensionId });

    [McpServerTool(Name = "unity_extension_call")]
    [Description("执行已注册的扩展操作。先用 unity_extension_catalog 查询契约；Spine 和 FairyGUI 查询同包内置，项目自行注册业务操作。写操作必须显式 allowWrite=true。")]
    public Task<CallToolResult> Call(string extensionId, string operation, JsonElement? arguments = null, bool allowWrite = false)
        => Send("unity.extensionCall", new { extensionId, operation, arguments, allowWrite });

    [McpServerTool(Name = "unity_read_component", ReadOnly = true)]
    [Description("分页读取组件的 Unity 序列化属性（含私有 SerializeField、嵌套路径、资源引用）。不执行任意 C# getter。相同类型的多个组件用 componentIndex 选择。")]
    public Task<CallToolResult> ReadComponent(string componentType, int instanceId = 0, string? path = null, string? name = null,
        int componentIndex = 0, string? propertyPath = null, int offset = 0, int limit = 50, int maxDepth = 2)
        => Send("unity.readComponent", new { componentType, instanceId, path, name, componentIndex, propertyPath, offset, limit, maxDepth });

    [McpServerTool(Name = "unity_capture_camera")]
    [Description("将指定 Camera 显式渲染为 PNG，返回本工程 Library/KabuUnityMcp/Captures 下绝对路径及帧号。16..2048 像素。恢复 Camera/RenderTexture 状态；会执行渲染回调。不含屏幕覆盖 UI，不等于 GameView 截图。")]
    public Task<CallToolResult> CaptureCamera(int cameraInstanceId, int width = 1280, int height = 720)
        => Send("unity.captureCamera", new { cameraInstanceId, width, height });

    private async Task<CallToolResult> Send(string method, object arguments)
    {
        try
        {
            var result = await hub.SendRequestAsync(method, arguments);
            return new CallToolResult { IsError = result == null,
                Content = [new TextContentBlock { Text = result?.ToString() ?? "Unity 未返回结果。" }] };
        }
        catch (Exception exception)
        {
            return new CallToolResult { IsError = true,
                Content = [new TextContentBlock { Text = exception.Message }] };
        }
    }
}
