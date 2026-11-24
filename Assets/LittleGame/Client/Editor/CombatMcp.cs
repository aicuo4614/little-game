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
        public string Version => "1.3.0";
        public string Description => "横向无限远征军团：无城墙无栅栏、整体白色布阵框自由拖放、职业差异化移速与转身速率、波后快速跑回白框、bump 风格平滑跟镜、十字中线招募与自走棋人口上限；所有操作调用玩家共用规则。";
        public string UnavailableReason => !Application.isPlaying || ClientBootstrap.Current == null ? "打开 Little Game/Combat/Open Combat 并进入Play" : null;
        private static JObject Schema(string properties = "", string required = "") => JObject.Parse("{'type':'object','properties':{" + properties + "},'required':[" + required + "],'additionalProperties':false}");
        private const string Unit = "'unitId':{'type':'integer','minimum':1}";
        private const string Lane = "'lane':{'type':'integer','minimum':0,'maximum':2}";
        public McpExtensionOperation[] Operations => new[]
        {
            new McpExtensionOperation("get_state", "读取横向远征阶段、白色布阵框边界、归队状态、推进里程、存活军团、狂暴状态、人口上限与经验圆环、招募次数/保底/十字中奖线、军团/敌人(含homeX/homeZ/facingYaw/turnSpeed/moveSpeed/canMerge)、冷却、战术、首领预警、胜负与最近事件", true, Schema()),
            new McpExtensionOperation("get_view", "读取UI主题/缺失素材、全局悬浮Tip(红/金)、按钮坐标与宽高、单位/阵位坐标、相机X位置、单位模型与动作/播放时间、技能时间轴统计、材质Shader支持及无限横向滚动场景状态；坐标左下原点0到1", true, Schema()),
            new McpExtensionOperation("get_events", "读取sequence之后的实际规则事件；重复轮询不推进时间。缓存保留最近600条，返回oldestSequence供识别缺口", true, Schema("'after':{'type':'integer','minimum':0}")),
            new McpExtensionOperation("get_definitions", "查看职业分支、移动速度、转身速率、白色布阵框范围、奖励及规则版本；不改变状态", true, Schema()),
            new McpExtensionOperation("restart", "玩家重开同样调用，清空本局；seed为可复现随机种子，默认173", false, Schema("'seed':{'type':'integer'}")),
            new McpExtensionOperation("spin", "正常转盘：每波3次，援军令后4次，十字中线（中横排与中竖排）三同出兵，十字双连出2兵；两次空转后下次保底", false, Schema()),
            new McpExtensionOperation("deploy", "在整体白色布阵框内自由部署或替换单位；支持传世界坐标 x/z（超白框拒绝）或参考 lane/row(0..2)；准备阶段限定，受自走棋人口上限(6→9)约束", false, Schema(Unit + "," + Lane + ",'row':{'type':'integer','minimum':0,'maximum':2},'x':{'type':'number'},'z':{'type':'number'}", "'unitId'")),
            new McpExtensionOperation("recall", "召回候补；至少保留一名上阵、候补容量6", false, Schema(Unit, "'unitId'")),
            new McpExtensionOperation("recycle", "遣散候补，不能直接删除上阵兵；仅用于处理容量，无材料收益", false, Schema(Unit, "'unitId'")),
            new McpExtensionOperation("merge", "三个同职业同星不同单位合成，保留unitId的位置与分支，消耗otherId和thirdId；最高3星", false, Schema(Unit + ",'otherId':{'type':'integer','minimum':1},'thirdId':{'type':'integer','minimum':1}", "'unitId','otherId','thirdId'")),
            new McpExtensionOperation("choose_branch", "二星后必须选一次合法职业分支，之后锁定；从get_definitions读职业选项", false, Schema(Unit + ",'branch':{'type':'string','enum':['Bastion','WarCry','Pierce','Gale','StormPool','ChainLightning','DeepFreeze','IceArmor']}", "'unitId','branch'")),
            new McpExtensionOperation("start_wave", "正常开战，未用次数放弃；暂存未处理或分支未选会拒绝。全军自由转向自动交锋", false, Schema()),
            new McpExtensionOperation("next_wave", "仅波后进入下一波，全员满血恢复推进下一段、刷新次数，不能跳过奖励或战斗", false, Schema()),
            new McpExtensionOperation("choose_reward", "第4/8波后选择尚未持有的局内强化；援军令不可叠加", false, Schema("'reward':{'type':'string','enum':['Reinforcements','WideStorm','LongFreeze']}", "'reward'")),
            new McpExtensionOperation("aim", "进入集火或护盾保护瞄准，暂停战斗但不扣次数；取消可恢复", false, Schema("'kind':{'type':'string','enum':['Focus','Protect']}", "'kind'")),
            new McpExtensionOperation("focus", "瞄准Focus后选存活敌人，消耗本波唯一战术；射程内优先转向输出", false, Schema(Unit, "'unitId'")),
            new McpExtensionOperation("protect", "瞄准Protect后选0到2区域(上/中/下行)，给该区域存活友军4秒28%护盾；空区域拒绝且不扣次数", false, Schema(Lane, "'lane'")),
            new McpExtensionOperation("cancel_aim", "取消瞄准，不扣战术点", false, Schema()),
            new McpExtensionOperation("pause", "正常暂停或继续，仅战斗阶段", false, Schema("'paused':{'type':'boolean'}", "'paused'")),
            new McpExtensionOperation("set_speed", "玩家同款1/2倍速度；不是调试跳帧或直接结算", false, Schema("'speed':{'type':'integer','enum':[1,2]}", "'speed'")),
            new McpExtensionOperation("pointer", "共用触屏/鼠标入口：begin/move/end/cancel；用于验证按钮、选中、起拖、白框落位、越界失败与取消；坐标左下0到1", false, Schema("'phase':{'type':'string','enum':['begin','move','end','cancel']},'x':{'type':'number','minimum':0,'maximum':1},'y':{'type':'number','minimum':0,'maximum':1}", "'phase','x','y'")),
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
                case "get_definitions":
                    return McpHostResult.Success(new
                    {
                        rulesVersion = "chat-approved-20261124-free-white-box-movespeed",
                        sameRuleEntry = "BattleApplication.Submit -> BattleSession.Execute",
                        deployBox = new { minX = BattleDefinitions.DeployMinX, maxX = BattleDefinitions.DeployMaxX, minZ = BattleDefinitions.DeployMinZ, maxZ = BattleDefinitions.DeployMaxZ },
                        professions = Enum.GetValues(typeof(TroopKind)).Cast<TroopKind>().Select(k => new
                        {
                            kind = k.ToString(),
                            name = BattleDefinitions.Name(k),
                            moveSpeed = BattleDefinitions.MoveSpeed(k),
                            turnSpeed = BattleDefinitions.TurnSpeed(k),
                            branches = Enum.GetValues(typeof(SkillBranch)).Cast<SkillBranch>().Where(b => BattleDefinitions.ValidBranch(k, b)).Select(b => new { id = b.ToString(), name = BattleDefinitions.Name(b) })
                        }),
                        rules = new
                        {
                            layout = "horizontal_free_white_box_infinite_scroll",
                            noGateHp = true,
                            noFences = true,
                            postWaveRegroupRun = true,
                            failureCondition = "all_deployed_troops_dead",
                            waveTimeoutEnrage = BattleDefinitions.WaveTimeout,
                            turnSpeedGatedAttack = BattleDefinitions.AttackAngleTolerance,
                            spinLine = "cross_center_row_and_col",
                            popStart = 6,
                            popMax = 9,
                            popExpPerWave = 2,
                            spins = 3,
                            reinforcedSpins = 4,
                            pity = 3,
                            mergeCount = 3,
                            reserveCapacity = 6,
                            waves = 12,
                            firstTwoSpins = "explicit_teaching_presets"
                        }
                    });
                case "capture_view": return McpHostResult.Success(CombatCapture.Capture(boot));
                case "performance": return McpHostResult.Success(boot.GetComponent<CombatDiagnostics>().Snapshot());
                case "pointer":
                    float x = (float)args["x"], y = (float)args["y"];
                    if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) || x < 0 || x > 1 || y < 0 || y > 1) return McpHostResult.Fail("坐标必须为0到1的有限数");
                    boot.Input.Pointer((string)args["phase"], new Vector2(x * Screen.width, y * Screen.height));
                    return McpHostResult.Success(new { inputDispatched = true, state = session.Snapshot() });
            }
            var c = new BattleCommand { UnitId = (int?)args["unitId"] ?? 0, OtherId = (int?)args["otherId"] ?? 0, ThirdId = (int?)args["thirdId"] ?? 0, Lane = (int?)args["lane"] ?? 0, Row = (int?)args["row"] ?? 0 };
            if (args["x"] != null && args["z"] != null) { c.UseWorldPos = true; c.TargetX = (float)args["x"]; c.TargetZ = (float)args["z"]; }
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
            var path = Path.GetFullPath("Library/KabuUnityMcp/Captures/combat-" + Guid.NewGuid().ToString("N") + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); ScreenCapture.CaptureScreenshot(path);
            return new { path, width = Screen.width, height = Screen.height, pending = true, note = "实际GameView帧，下一帧写入；调用方等待文件存在后读取。非真机截图" };
        }
    }
}
