using System.ComponentModel;
using KabuUnityMcp.Server.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KabuUnityMcp.Server.Tools.Unity.Combat;

/// <summary>本项目专用工具。仅转发到同一扩展契约，规则仍由玩家共用入口校验。</summary>
[McpServerToolType]
public sealed class CombatTools(UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_combat_get_state", ReadOnly = true)]
    [Description("读取本工程波次、小波出怪、英雄身份、羁绊、单位法力与技能、本波与本局伤害统计、胜负和可用操作。先确认工程路径。")]
    public Task<CallToolResult> State() => Send("get_state", new { }, false);

    [McpServerTool(Name = "unity_combat_get_view", ReadOnly = true)]
    [Description("读取UI主题/缺失素材、按钮坐标与宽高、军团和阵位坐标（左下原点0到1），以及单位模型、动作/播放时间和Shader支持，用于共用输入与表现验证。")]
    public Task<CallToolResult> View() => Send("get_view", new { }, false);

    [McpServerTool(Name = "unity_combat_get_events", ReadOnly = true)]
    [Description("读取指定sequence之后的实际战斗事件；返回缓存最早/最新序号，不推进或调试战斗。")]
    public Task<CallToolResult> Events(long after = 0) => Send("get_events", new { after }, false);

    [McpServerTool(Name = "unity_combat_wait_for_event", ReadOnly = true)]
    [Description("等待真实战斗自然推进产生的指定事件，例如wave_cleared/boss_telegraph/run_won/run_lost；不跳帧、不改数值。timeoutMs最多60000。")]
    public async Task<CallToolResult> WaitForEvent(string eventType, long after = 0, int timeoutMs = 10000)
    {
        if (string.IsNullOrWhiteSpace(eventType) || after < 0 || timeoutMs < 1 || timeoutMs > 60000)
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "事件名必填，after非负，timeoutMs为1到60000" }] };
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            do
            {
                var reply = await hub.SendRequestAsync("unity.extensionCall", new { extensionId = "little-game.combat", operation = "get_events", arguments = new { after }, allowWrite = false });
                using var json = System.Text.Json.JsonDocument.Parse(reply?.ToString() ?? "{}");
                if (json.RootElement.TryGetProperty("events", out var events))
                    foreach (var item in events.EnumerateArray())
                    {
                        var type = item.TryGetProperty("Type", out var a) ? a : item.TryGetProperty("type", out var b) ? b : default;
                        if (type.ValueKind == System.Text.Json.JsonValueKind.String && type.GetString() == eventType)
                            return new CallToolResult { Content = [new TextContentBlock { Text = System.Text.Json.JsonSerializer.Serialize(new { matched = true, elapsedMs = timer.ElapsedMilliseconds, result = json.RootElement.Clone() }) }] };
                    }
                await Task.Delay(Math.Min(200, Math.Max(1, timeoutMs - (int)timer.ElapsedMilliseconds)));
            } while (timer.ElapsedMilliseconds < timeoutMs);
            return new CallToolResult { Content = [new TextContentBlock { Text = System.Text.Json.JsonSerializer.Serialize(new { matched = false, timeout = true, elapsedMs = timer.ElapsedMilliseconds }) }] };
        }
        catch (Exception e) { return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = e.Message }] }; }
    }

    [McpServerTool(Name = "unity_combat_restart")]
    [Description("正常重开军团远征，清空本局军团与奖励；seed固定可复现。不是跳波或数值调试。")]
    public Task<CallToolResult> Restart(int seed = 173) => Send("restart", new { seed });

    [McpServerTool(Name = "unity_combat_spin")]
    [Description("正常转盘招募一次，每波3次或援军令4次，十字中线三同后随机发放对应职业英雄，连续两空后下次保底。")]
    public Task<CallToolResult> Spin() => Send("spin", new { });

    [McpServerTool(Name = "unity_combat_deploy")]
    [Description("准备阶段将军团部署到lane/row各0到2的阵位，占用格交换；与玩家拖动共用规则。")]
    public Task<CallToolResult> Deploy(int unitId, int lane, int row) => Send("deploy", new { unitId, lane, row });

    [McpServerTool(Name = "unity_combat_merge")]
    [Description("三个同名同星英雄合成，unitId为宿主，另外两名消耗；最高三星。")]
    public Task<CallToolResult> Merge(int unitId, int otherId, int thirdId) => Send("merge", new { unitId, otherId, thirdId });

    [McpServerTool(Name = "unity_combat_get_definitions", ReadOnly = true)]
    [Description("读取当前关卡、英雄和怪物配表定义、招募池与满蓝技能优先级。")]
    public Task<CallToolResult> Definitions() => Send("get_definitions", new { }, false);

    [McpServerTool(Name = "unity_combat_start_wave")]
    [Description("双方以0法力开战，第一小波立即出怪，后续按配表间隔入场。未使用转盘次数放弃，未处理暂存会拒绝。")]
    public Task<CallToolResult> StartWave() => Send("start_wave", new { });

    [McpServerTool(Name = "unity_combat_next_wave")]
    [Description("波后正常进入下一波，恢复军团生命和招募次数；不能跳过战斗或奖励。")]
    public Task<CallToolResult> NextWave() => Send("next_wave", new { });

    [McpServerTool(Name = "unity_combat_choose_reward")]
    [Description("第4/8波后选局内强化：Reinforcements/WideStorm/LongFreeze，不能重复获取。")]
    public Task<CallToolResult> Reward(string reward) => Send("choose_reward", new { reward });

    [McpServerTool(Name = "unity_combat_pointer")]
    [Description("玩家共用触屏/鼠标入口，phase为begin/move/end/cancel，x/y为左下原点0到1。包含命中、起拖、取消与松手验证。")]
    public Task<CallToolResult> Pointer(string phase, float x, float y) => Send("pointer", new { phase, x, y });

    private async Task<CallToolResult> Send(string operation, object arguments, bool allowWrite = true)
    {
        try
        {
            var response = await hub.SendRequestAsync("unity.extensionCall", new { extensionId = "little-game.combat", operation, arguments, allowWrite });
            return new CallToolResult { IsError = response == null, Content = [new TextContentBlock { Text = response?.ToString() ?? "Unity未返回战斗结果" }] };
        }
        catch (Exception e) { return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = e.Message }] }; }
    }
}
