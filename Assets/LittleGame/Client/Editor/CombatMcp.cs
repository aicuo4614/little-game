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
        public string Version => "2.4.0";
        public string Description => "符阵军团英雄配表、羁绊、法力技能、NavMeshAgent战斗和第一关十波；所有操作调用玩家共用规则。";
        public string UnavailableReason => !Application.isPlaying || ClientBootstrap.Current == null ? "打开 Little Game/Combat/Open Combat 并进入Play" : null;
        private static JObject Schema(string properties = "", string required = "") => JObject.Parse("{'type':'object','properties':{" + properties + "},'required':[" + required + "],'additionalProperties':false}");
        private const string Unit = "'unitId':{'type':'integer','minimum':1}";
        private const string Lane = "'lane':{'type':'integer','minimum':0,'maximum':2}";
        public McpExtensionOperation[] Operations => new[]
        {
            new McpExtensionOperation("get_state", "读取横向远征阶段、白色布阵框边界、归队状态、推进里程、存活军团、狂暴状态、人口上限与经验、招募次数/保底/十字中奖线、军团/敌人(含homeX/homeZ/facingYaw/turnSpeed/moveSpeed/canMerge)、技能实例(impactAt/timingMode/projectileSpeed)、每单位法力/星级技能/独立冷却/状态/属性、羁绊、出怪小波、伤害统计（按英雄归属、本波及整局实际扣血）、首领预警、胜负与事件", true, Schema()),
            new McpExtensionOperation("get_view", "读取UI主题/缺失素材、全局悬浮Tip(红/金)、候补拖回热区、按钮坐标与宽高、单位/阵位坐标、固定朝向机位模式、实际基础偏航(cameraYaw)、相机X及前移边界、道路入口、单位模型与动作/播放时间、技能时间轴统计与active.visualImpactAt、材质Shader支持及无限横向滚动场景状态；坐标左下原点0到1", true, Schema()),
            new McpExtensionOperation("get_navigation", "只读查询原生NavMesh路径、阻挡原因、FSM目标、实际速度和导航适配层累计CPU耗时；unitId可选，不推进时间", true, Schema(Unit)),
            new McpExtensionOperation("get_events", "读取sequence之后的实际规则事件；重复轮询不推进时间。缓存保留最近600条，返回oldestSequence供识别缺口", true, Schema("'after':{'type':'integer','minimum':0}")),
            new McpExtensionOperation("get_definitions", "查看英雄、怪物、招募池、羁绊、波次表和规则版本；不改变状态", true, Schema()),
            new McpExtensionOperation("restart", "玩家重开同样调用，清空本局；seed为可复现随机种子，默认173", false, Schema("'seed':{'type':'integer'}")),
            new McpExtensionOperation("spin", "正常转盘：每波3次，援军令后4次，十字中线（中横排与中竖排）三同出兵，十字双连出2兵；两次空转后下次保底；从中奖职业随机发放英雄；凑齐三名同名同星英雄自动合成升星", false, Schema()),
            new McpExtensionOperation("deploy", "在整体白色布阵框内自由部署或替换单位；传世界坐标 x/z 时超出白框自动夹紧到最近合法点 A；也支持参考 lane/row(0..2)；准备阶段限定，受人口上限(6→9)约束", false, Schema(Unit + "," + Lane + ",'row':{'type':'integer','minimum':0,'maximum':2},'x':{'type':'number'},'z':{'type':'number'}", "'unitId'")),
            new McpExtensionOperation("recall", "召回候补（玩家通过将场上单位拖入候补区触发）；至少保留一名上阵、候补容量6", false, Schema(Unit, "'unitId'")),
            new McpExtensionOperation("recycle", "遣散候补，不能直接删除上阵兵；仅用于处理容量，无材料收益", false, Schema(Unit, "'unitId'")),
            new McpExtensionOperation("merge", "手动请求合成三个同名同星英雄（正常招募会自动合成）；最高3星", false, Schema(Unit + ",'otherId':{'type':'integer','minimum':1},'thirdId':{'type':'integer','minimum':1}", "'unitId','otherId','thirdId'")),
            new McpExtensionOperation("start_wave", "正常开战，未用次数放弃；暂存未处理会拒绝。双方以0法力开始，小波按表中时间入场。全军原生NavMeshAgent导航、攻击落点预占与阻挡等待，规则状态机驱动交锋", false, Schema()),
            new McpExtensionOperation("next_wave", "仅波后进入下一波，全员满血恢复推进下一段、刷新次数，不能跳过奖励或战斗", false, Schema()),
            new McpExtensionOperation("choose_reward", "第4/8波后选择尚未持有的局内强化；援军令不可叠加", false, Schema("'reward':{'type':'string','enum':['Reinforcements','WideStorm','LongFreeze']}", "'reward'")),
            new McpExtensionOperation("pause", "正常暂停或继续，仅战斗阶段", false, Schema("'paused':{'type':'boolean'}", "'paused'")),
            new McpExtensionOperation("set_speed", "玩家同款1/2倍速度；不是调试跳帧或直接结算", false, Schema("'speed':{'type':'integer','enum':[1,2]}", "'speed'")),
            new McpExtensionOperation("pointer", "共用触屏/鼠标入口：begin/move/end/cancel；用于验证tap即选中、点布阵框外空地取消选中、滑空地不改变机位、拖拽吸附最近点A、拖回候补召回、双击候补上场与取消；坐标左下0到1", false, Schema("'phase':{'type':'string','enum':['begin','move','end','cancel']},'x':{'type':'number','minimum':0,'maximum':1},'y':{'type':'number','minimum':0,'maximum':1}", "'phase','x','y'")),
            new McpExtensionOperation("capture_view", "保存实际场景和HUD合成渲染，540x960；编辑器截图不是手机截图", true, Schema()),
            new McpExtensionOperation("profile_capture", "编辑器诊断：start 解除限帧和垂直同步并记录逐帧 CPU/GC，stop 保存并恢复，status 只读状态；不修改战斗规则", false, Schema("'action':{'type':'string','enum':['start','stop','status']}", "'action'")),
            new McpExtensionOperation("performance", "最近240帧的帧间隔、尺寸和平台；编辑器数据不得作为手机帧率", true, Schema())
        };
        public McpHostResult Execute(string operation, JObject args)
        {
            var boot = ClientBootstrap.Current; var session = boot.App.Session;
            switch (operation)
            {
                case "profile_capture":
                    return McpHostResult.Success((string)args["action"] == "start" ? CombatPerformanceCapture.Start() : (string)args["action"] == "stop" ? CombatPerformanceCapture.Stop() : CombatPerformanceCapture.Status());
                case "get_state": return McpHostResult.Success(session.Snapshot());
                case "get_view": return McpHostResult.Success(boot.Hud.Snapshot());
                case "get_events": var events = session.EventsAfter(0).ToArray(); return McpHostResult.Success(new { oldestSequence = events.FirstOrDefault()?.Sequence ?? 0, lastSequence = session.LastSequence, events = session.EventsAfter((long?)args["after"] ?? 0) });
                case "get_navigation":
                {
                    int filterId = (int?)args["unitId"] ?? 0;
                    return McpHostResult.Success(new
                    {
                        algorithm = boot.App.Navigation?.Backend ?? "unbound",
                        rvoActive = boot.App.Navigation != null,
                        fixedStep = BattleDefinitions.Step,
                        cpuTotalMs = session.NavigationCpuTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                        units = session.Troops.Where(t => t.Deployed && (filterId == 0 || t.Id == filterId)).Select(t => new
                            { id = t.Id, aiState = t.AiState.ToString(), targetId = t.TargetId, x = t.X, z = t.Z, vx = t.Vx, vz = t.Vz, navigation = t.Navigation.Snapshot() }),
                        enemies = session.Enemies.Where(e => filterId == 0 || e.Id == filterId).Select(e => new
                            { id = e.Id, aiState = e.AiState.ToString(), targetId = e.TargetId, x = e.X, z = e.Z, vx = e.Vx, vz = e.Vz, navigation = e.Navigation.Snapshot() }),
                        native = boot.App.Navigation?.Snapshot()
                    });
                }
                case "get_definitions":
                    return McpHostResult.Success(new
                    {
                        rulesVersion = "current-20261127-hero-tables",
                        skillTiming = "弹道按实际施法距离与资源挂点计算到达时间；get_state.skills 返回 timingMode/projectileSpeed/impactAt，get_view.presentation.skillTimeline.active 返回 visualImpactAt。预览仍不结算伤害。",
                        sameRuleEntry = "BattleApplication.Submit -> BattleSession.Execute",
                        deployBox = new { minX = BattleDefinitions.DeployMinX, maxX = BattleDefinitions.DeployMaxX, minZ = BattleDefinitions.DeployMinZ, maxZ = BattleDefinitions.DeployMaxZ },
                        professions = Enum.GetValues(typeof(UnitJob)).Cast<UnitJob>().Select(job => new
                        {
                            kind = job.ToString(), name = BattleDefinitions.Name((TroopKind)job), recruits = BattleTables.Current.RecruitPool(job)
                        }),
                        units = BattleTables.Current.Units.Select(u => new
                        {
                            id = u.Id, name = u.Name, job = u.Job.ToString(), race = u.Race.ToString(), grade = u.Grade.ToString(), role = u.Role,
                            health = u.Hp, attack = u.Attack, armor = u.Armor, magicResist = u.Resist, attackSpeed = u.AttacksPerSecond, moveSpeed = u.Move, range = u.Range,
                            skills = u.Skills.Select(s => s == null ? null : new { id = s.Id, name = s.Name, passive = s.Passive, cooldown = s.Cooldown })
                        }),
                        stage = Enumerable.Range(1, BattleDefinitions.TotalWaves).Select(w => new
                        {
                            wave = w, boss = BattleTables.Current.IsBoss(w), maxDeployedOverride = BattleTables.Current.DeployCap(w),
                            reward = BattleTables.Current.HasReward(w), enemySynergy = BattleTables.Current.HasEnemySynergy(w),
                            forecast = BattleTables.Current.Schedule(w, session.Seed).Select(e =>
                                new { kind = e.MonsterId, e.Batch, e.Star, e.Lane, e.At })
                        }),
                        synergies = BattleTables.Current.Synergies.Select(s => new
                        {
                            race = s.Race?.ToString(), job = s.Job?.ToString(), s.Count, stat = s.Stat.ToString(), s.Amount, s.Duration
                        }),
                        rules = new
                        {
                            layout = "horizontal_free_white_box_infinite_scroll",
                            avoidance = "unity_navmesh_agent_attack_slots",
                            navigation = "unity_navmesh_agent_attack_slots",
                            rvoActive = true,
                            blockedWait = true,
                            actualDisplacementVelocity = true,
                            autoMerge = true,
                            manaMax = BattleCombatant.MaxMana,
                            castPriority = new[] { 3, 2, 1 },
                            mergeIdentity = "heroId_and_star",
                            clampOutOfBoxDeploy = true,
                            doubleTapReserveDeploy = true,
                            dragToReserveRecall = true,
                            tapImmediateSelect = true,
                            selectionActionPanel = false,
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
                            waves = BattleDefinitions.TotalWaves,
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
                case "start_wave": c.Kind = CommandKind.StartWave; break;
                case "next_wave": c.Kind = CommandKind.NextWave; break;
                case "choose_reward": c.Kind = CommandKind.ChooseReward; c.Reward = (RewardKind)Enum.Parse(typeof(RewardKind), (string)args["reward"]); break;
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
