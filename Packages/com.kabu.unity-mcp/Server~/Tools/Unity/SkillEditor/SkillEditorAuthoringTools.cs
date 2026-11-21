using System.ComponentModel;
using System.Text.Json;
using KabuUnityMcp.Server.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KabuUnityMcp.Server.Tools.Unity.SkillEditor;

[McpServerToolType]
public class SkillEditorAuthoringTools(UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_skill_editor_catalog", ReadOnly = true)]
    [Description("获取技能编辑器 Clip 类型、用途、字段、默认配置、上下文动作和命令。")]
    public Task<CallToolResult> Catalog(
        [Description("可选 Clip 类型名，例如 PlaySpine")] string? type = null)
        => Request("Catalog", new { type });

    [McpServerTool(Name = "unity_skill_editor_read_timeline", ReadOnly = true)]
    [Description("读取当前编辑缓冲区的 Timeline、Clip、事件、节点、连线、有效时间和 revision。")]
    public Task<CallToolResult> ReadTimeline() => Request("Read", new { });

    [McpServerTool(Name = "unity_skill_editor_list_actions", ReadOnly = true)]
    [Description("查询当前对象可执行的右键等价动作和禁用原因。target 为 background、clip、event、head 或 tail。")]
    public Task<CallToolResult> ListActions(string target = "background", string? key = null)
        => Request("Actions", new { target, key });

    [McpServerTool(Name = "unity_skill_editor_edit")]
    [Description("按 revision 批量编排技能。先使用 catalog 查动作，再 dryRun 检查。应用仅修改编辑缓冲区。")]
    public Task<CallToolResult> Edit(
        [Description("read_timeline 返回的编辑版本")] string revision,
        [Description("操作数组。每项包含 action 及该动作参数，新增项可用 alias 在同批通过 $alias 引用。")] JsonElement[] operations,
        [Description("只返回拟议结果")] bool dryRun = true)
        => Request("Edit", new { revision, operations, dryRun });

    [McpServerTool(Name = "unity_skill_editor_command")]
    [Description("执行技能编辑器文档和预览命令。catalog 返回完整命令和参数说明。")]
    public Task<CallToolResult> Command(
        [Description("命令 ID，例如 new、save、undo、validate、assets、select")] string command,
        [Description("命令参数对象")] JsonElement? arguments = null)
        => Request("Command", new { command, arguments });

    private async Task<CallToolResult> Request(string operation, object arguments)
    {
        try
        {
            var result = await hub.SendRequestAsync("unity.skillEditorAuthoring" + operation, arguments);
            return new CallToolResult
            {
                IsError = result is null,
                Content = [new TextContentBlock { Text = result?.ToString() ?? "Unity 未返回结果。" }]
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = ex.Message }]
            };
        }
    }
}
