using System;
using System.IO;
using System.Linq;
using LittleGame.Combat;
using Newtonsoft.Json.Linq;
using KabuUnityMcp.Editor.Extensions;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor
{
    /// <summary>扩展只作协议适配，不另写招募、伤害、培养或胜负规则。</summary>
    [InitializeOnLoad]
    public sealed class CombatMcp : IMcpExtension
    {
        private static readonly CombatMcp instance = new CombatMcp();
        static CombatMcp() { McpExtensionRegistry.Register(instance); AssemblyReloadEvents.beforeAssemblyReload += () => McpExtensionRegistry.Unregister(instance); }
        public string Id => "little-game.combat";
        public string Version => "1.0.3";
        public string Description => "军团远征：横线招募、三兵合成、分支、三路布阵、自动战斗、一次战术和奖励；所有操作调用玩家共用规则。";
        public string UnavailableReason => !Application.isPlaying || ClientBootstrap.Current == null ? "打开 Little Game/Combat/Open Combat 并进入Play" : null;
        private static JObject Schema(string properties = "", string required = "") => JObject.Parse("{'type':'object','properties':{" + properties + "},'required':[" + required + "],'additionalProperties':false}");
        private const string Unit = "'unitId':{'type':'integer','minimum':1}";
        private const string Lane = "'lane':{'type':'integer','minimum':0,'maximum':2}";
        public McpExtensionOperation[] Operations => new[]
        {
            new McpExtensionOperation("get_state", "读取阶段、敌情、招募次数/保底、军团/敌人、冷却、分栏、战术、首领预警、胜负、可用操作与最近事件", true, Schema()),
            new McpExtensionOperation("get_view", "读取UI主题/缺失素材、按钮坐标与宽高、单位/阵位坐标、单位模型与动作/播放时间、材质Shader支持及隔栏状态；坐标左下原点0到1", true, Schema()),
            new McpExtensionOperation("get_events", "读取sequence之后的实际规则事件；重复轮询不推进时间。缓存保留最近600条，返回oldestSequence供识别缺口", true, Schema("'after':{'type':'integer','minimum':0}")),
            new McpExtensionOperation("get_definitions", "查看职业分支、射程、当前敌情、奖励及规则版本；不改变状态", true, Schema()),
            new McpExtensionOperation("restart", "玩家重开同样调用，清空本局；seed为可复现随机种子，默认173", false, Schema("'seed':{'type':'integer'}")),
            new McpExtensionOperation("spin", "正常转盘：每波3次，援军令后4次，仅横向三同出兵；两次空转后的下次保底。教学前两转明确预设", false, Schema()),
            new McpExtensionOperation("deploy", "部署或交换阵位；准备阶段限定，候补区与战场同一规则", false, Schema(Unit + "," + Lane + ",'row':{'type':'integer','minimum':0,'maximum':2}", "'unitId','lane','row'")),
            new McpExtensionOperation("recall", "召回候补；至少保留一名上阵、候补容量6", false, Schema(Unit, "'unitId'")),
            new McpExtensionOperation("recycle", "遣散候补，不能直接删除上阵兵；仅用于处理容量，无材料收益", false, Schema(Unit, "'unitId'")),
            new McpExtensionOperation("merge", "三个同职业同星不同单位合成，保留unitId的位置与分支，消耗otherId和thirdId；最高3星", false, Schema(Unit + ",'otherId':{'type':'integer','minimum':1},'thirdId':{'type':'integer','minimum':1}", "'unitId','otherId','thirdId'")),
            new McpExtensionOperation("choose_branch", "二星后必须选一次合法职业分支，之后锁定；从get_definitions读职业选项", false, Schema(Unit + ",'branch':{'type':'string','enum':['Bastion','WarCry','Pierce','Gale','StormPool','ChainLightning','DeepFreeze','IceArmor']}", "'unitId','branch'")),
            new McpExtensionOperation("start_wave", "正常开战，未用次数放弃；暂存未处理或分支未选会拒绝。战斗自动推进", false, Schema()),
            new McpExtensionOperation("next_wave", "仅波后进入下一波，满血恢复、刷新次数，不能跳过奖励或战斗", false, Schema()),
            new McpExtensionOperation("choose_reward", "第4/8波后选择尚未持有的局内强化；援军令不可叠加", false, Schema("'reward':{'type':'string','enum':['Reinforcements','WideStorm','LongFreeze']}", "'reward'")),
            new McpExtensionOperation("aim", "进入集火或保护瞄准，暂停战斗但不扣次数；取消可恢复", false, Schema("'kind':{'type':'string','enum':['Focus','Protect']}", "'kind'")),
            new McpExtensionOperation("focus", "瞄准Focus后选存活敌人，消耗本波唯一战术；不突破实际射程", false, Schema(Unit, "'unitId'")),
            new McpExtensionOperation("protect", "瞄准Protect后选0到2路，给存活该路友军4秒25%护盾；空路拒绝且不扣次数", false, Schema(Lane, "'lane'")),
            new McpExtensionOperation("cancel_aim", "取消瞄准，不扣战术点", false, Schema()),
            new McpExtensionOperation("pause", "正常暂停或继续，仅战斗阶段", false, Schema("'paused':{'type':'boolean'}", "'paused'")),
            new McpExtensionOperation("set_speed", "玩家同款1/2倍速度；不是调试跳帧或直接结算", false, Schema("'speed':{'type':'integer','enum':[1,2]}", "'speed'")),
            new McpExtensionOperation("pointer", "共用触屏/鼠标入口：begin/move/end/cancel；用于验证按钮、选中、起拖、松手和取消；坐标左下0到1", false, Schema("'phase':{'type':'string','enum':['begin','move','end','cancel']},'x':{'type':'number','minimum':0,'maximum':1},'y':{'type':'number','minimum':0,'maximum':1}", "'phase','x','y'")),
            new McpExtensionOperation("capture_view", "保存实际场景和HUD合成渲染，540x960；编辑器截图不是手机截图", true, Schema()),
            new McpExtensionOperation("performance", "最近240帧的帧间隔、尺寸和平台；编辑器数据不得作为手机帧率", true, Schema())
        };
        public McpHostResult Execute(string operation, JObject args)
        {
            var boot = ClientBootstrap.Current; var session = boot.App.Session;
            switch (operation)
            {
                case "get_state": return McpHostResult.Success(session.Snapshot());
                case "get_view": return McpHostResult.Success(boot.Hud.Snapshot());
                case "get_events": var events = session.EventsAfter(0).ToArray(); return McpHostResult.Success(new { oldestSequence = events.FirstOrDefault()?.Sequence ?? 0, lastSequence = session.LastSequence, events = session.EventsAfter((long?)args["after"] ?? 0) });
                case "get_definitions": return McpHostResult.Success(new { rulesVersion = "chat-approved-20261123", sameRuleEntry = "BattleApplication.Submit -> BattleSession.Execute", professions = Enum.GetValues(typeof(TroopKind)).Cast<TroopKind>().Select(k => new { kind = k.ToString(), name = BattleDefinitions.Name(k), branches = Enum.GetValues(typeof(SkillBranch)).Cast<SkillBranch>().Where(b => BattleDefinitions.ValidBranch(k, b)).Select(b => new { id = b.ToString(), name = BattleDefinitions.Name(b) }) }), rules = new { spinLine = "horizontal_three_equal", spins = 3, reinforcedSpins = 4, pity = 3, mergeCount = 3, reserveCapacity = 6, waves = 12, waveTimeout = 45, rangedSupport = "adjacent_lane_and_actual_range", boss = "remove_fences_melee_cross_lane", firstTwoSpins = "explicit_teaching_presets" } });
                case "capture_view": return McpHostResult.Success(CombatCapture.Capture(boot));
                case "performance": return McpHostResult.Success(boot.GetComponent<CombatDiagnostics>().Snapshot());
                case "pointer":
                    float x = (float)args["x"], y = (float)args["y"];
                    if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) || x < 0 || x > 1 || y < 0 || y > 1) return McpHostResult.Fail("坐标必须为0到1的有限数");
                    boot.Input.Pointer((string)args["phase"], new Vector2(x * Screen.width, y * Screen.height));
                    return McpHostResult.Success(new { inputDispatched = true, state = session.Snapshot() });
            }
            var c = new BattleCommand { UnitId = (int?)args["unitId"] ?? 0, OtherId = (int?)args["otherId"] ?? 0, ThirdId = (int?)args["thirdId"] ?? 0, Lane = (int?)args["lane"] ?? 0, Row = (int?)args["row"] ?? 0 };
            switch (operation)
            {
                case "restart": c.Kind = CommandKind.NewRun; c.Seed = (int?)args["seed"] ?? 173; boot.Hud.ClearSelection(); break;
                case "spin": c.Kind = CommandKind.Spin; break;
                case "deploy": c.Kind = CommandKind.Deploy; break;
                case "recall": c.Kind = CommandKind.Recall; break;
                case "recycle": c.Kind = CommandKind.Recycle; break;
                case "merge": c.Kind = CommandKind.Merge; break;
                case "choose_branch": c.Kind = CommandKind.ChooseBranch; c.Branch = (SkillBranch)Enum.Parse(typeof(SkillBranch), (string)args["branch"]); break;
                case "start_wave": c.Kind = CommandKind.StartWave; break;
                case "next_wave": c.Kind = CommandKind.NextWave; break;
                case "choose_reward": c.Kind = CommandKind.ChooseReward; c.Reward = (RewardKind)Enum.Parse(typeof(RewardKind), (string)args["reward"]); break;
                case "aim": c.Kind = CommandKind.Aim; c.Aim = (AimKind)Enum.Parse(typeof(AimKind), (string)args["kind"]); break;
                case "focus": c.Kind = CommandKind.Focus; break;
                case "protect": c.Kind = CommandKind.Protect; break;
                case "cancel_aim": c.Kind = CommandKind.CancelAim; break;
                case "pause": c.Kind = CommandKind.Pause; c.Flag = (bool)args["paused"]; break;
                case "set_speed": c.Kind = CommandKind.SetSpeed; c.Speed = (int)args["speed"]; break;
                default: return McpHostResult.Fail("未知战斗操作");
            }
            boot.Hud.Submit(c); var result = boot.App.LastResult;
            return McpHostResult.Success(new { accepted = result.Accepted, reason = result.Reason, state = boot.App.Session.Snapshot() });
        }
    }
    internal static class CombatCapture
    {
        public static object Capture(ClientBootstrap boot)
        {
            // 从实际GameView帧截取，避免额外URP相机覆盖或改变场景/UI的组合。
            var path = Path.GetFullPath("Library/KabuUnityMcp/Captures/combat-" + Guid.NewGuid().ToString("N") + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); ScreenCapture.CaptureScreenshot(path);
            return new { path, width = Screen.width, height = Screen.height, pending = true, note = "实际GameView帧，下一帧写入；调用方等待文件存在后读取。非真机截图" };
        }
    }
}
