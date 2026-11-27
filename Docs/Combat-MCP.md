# 战斗 MCP 操作与验证

> 2026-11-25 更新：已接替并继续 Demo 技术栈迁移，当前分层、启动、资源、技能编辑器与验收边界以 [Framework-Migration.md](Framework-Migration.md) 为准。下文关于“尚未迁入完整 TimelineEditor”、旧 little-game.skill-timeline 扩展和 Resources 技能镜像的描述属于历史状态；当前使用原生 17 个 unity_skill_editor_* 工具、标准 TimelineConfig 与共享 3D 播放器。规则状态 schema6。

端点 `http://127.0.0.1:3833/mcp`，工程 `E:/Project/little-game/Assets`。所有写操作均走 `BattleApplication.Submit → BattleSession.Execute`，与玩家相同，不存在跳波、直接改血或直接获胜的生产工具。

2026-11-27 战斗扩展更新为 **2.0.0**，状态 schema **7**。当前验收入口为 `Tools/Preparation/verify_hero_rework.py`。第一关共十波，第 5、10 波各一只石甲巨像。

## 发现与入口

1. `unity_get_project_info` 确认工程路径，`unity_get_compilation_status` 确认编译。
2. 退出 Play 后执行菜单 `Little Game/Combat/Open Combat`，再 `unity_enter_play_mode`。
3. `tools/list` 查看 `unity_combat_*` 直接工具。`unity_extension_catalog(extensionId="little-game.combat")` 提供完整扩展操作及参数。
4. `unity_combat_get_state` 读取小波、英雄身份、法力、技能解锁和冷却、羁绊、预警与事件。`unity_combat_get_view` 读取真实屏幕目标。`unity_combat_get_definitions` 读取关卡、英雄、怪物、技能与招募池。

冷启动诊断读取 get_view.presentation.framework。resourceMode 为 OfflineBuiltIn 时从内置资源清单启动，默认编辑器模拟为 EditorSimulateMode。packages 列出七个包的清单状态。

## 固定朝向机位（战斗扩展 1.9.0）

`get_view.presentation` 返回 `cameraMode`、`cameraYaw`、`cameraX`、`cameraBaseX`、`cameraMaxX` 及 `environment.entrance`。基础偏航为 0°，自动跟战仅沿 X 小幅平移，暂停或瞄准时停止跟随。空地滑动使用同一 pointer 入口验证机位稳定。道路入口是场景地标，敌军出现后血条随单位显示。

## 正常一局

| 操作 | 输入 | 条件与结果 |
| --- | --- | --- |
| `unity_combat_restart` | seed，可省略为173 | 正常重开，不保留本局军团与奖励；相同种子复现 |
| `unity_combat_spin` | 无 | 准备阶段、有次数、无暂存。十字中线三同后随机发放对应职业英雄，两次空转后的下一次保底 |
| `unity_combat_deploy` | unitId、lane、row | lane/row均0到2；未满人口时候补可上空格，满人口仅允许与已占格替换或调整已上阵位置 |
| `unity_combat_merge` | unitId、otherId、thirdId | 三个同名同星英雄，保留宿主位置，消耗另外两名，自动解锁下一技能 |
| `unity_combat_start_wave` | 无 | 暂存清空，至少一人上阵。敌我从 0 法力开战，第一小波立即出怪，剩余转动放弃 |
| `unity_combat_aim` | kind=Focus或Protect | 暂停瞄准，不扣次数 |
| `unity_combat_focus` | unitId为敌人ID | 存活目标；消耗本波唯一战术，射程不改变 |
| `unity_combat_protect` | lane | 区域内存活友军获得 4 秒、28% 最大生命值的护盾 |
| `unity_combat_choose_reward` | reward | 第4/8波后，选择未持有的 Reinforcements/WideStorm/LongFreeze |
| `unity_combat_next_wave` | 无 | 仅波后，无权跳过战斗或奖励；满血、刷新转盘 |

技能随星级固定解锁，满 100 法力按技能三、二、一的优先级选可用主动技能。波次结束需刷完所有小波并清空敌人。

玩家界面重开使用新的随机种子。MCP显式传入seed用于复现同一局；它只决定随机流，不绕过正常次数、掉落、战斗或培养规则。

## 补充扩展操作

以下通过 `unity_extension_call(extensionId="little-game.combat", operation=..., arguments=..., allowWrite=true)` 调用：

- `recall(unitId)`：召回候补，容量6且至少保留一名上阵。
- `recycle(unitId)`：只允许遣散候补，不产生材料收益，避免容量满时卡住。
- `cancel_aim()`：取消不扣次数。
- `pause(paused)`：战斗中的正常暂停/继续。
- `set_speed(speed)`：正常1倍或2倍播放，不是调试跳帧。
- `get_definitions()`：职业、分支和规则约束，只读。
- `capture_view()`：实际GameView帧截图，返回路径，下一帧写入，必须等待文件存在；不是手机截图。
- `performance()`：帧间隔、样本、分辨率和平台。编辑器数据不能写成手机FPS。

## 事件等待

`unity_combat_get_events(after)` 返回最近600条缓存中的新事件及最早/最新序号；若请求序号早于缓存最早序号，调用者应重读完整状态，不能假定事件没有发生。

`unity_combat_wait_for_event(eventType, after, timeoutMs)` 等待真实战斗自然推进，例如 `wave_cleared`、`boss_telegraph`、`run_won`、`run_lost`。上限60秒，超时返回 `matched=false`，不将超时当作失败或胜利。它不修改时钟、生成敌人或跳过关卡。

瞄准中或暂停中不会自然产生后续战斗事件；等待前应确认 `aim=None` 且 `paused=false`。

## 真实输入回放

`unity_combat_pointer(phase,x,y)` 与鼠标和手机触屏完全共用入口，原点左下，坐标归一化0到1，phase为begin/move/end/cancel。

从 `get_view.buttons` 找按钮中心；点击是begin→end；拖动从候补按钮或已部署单位中心开始，move到 `cells` 中的阵位，再end；取消使用cancel，之后end不能执行旧操作。两个触点、失焦和应用暂停都会清理手势。

直接调用deploy通过只能证明规则，必须另外通过pointer证明命中、起拖、取消与松手。编辑器pointer通过仍不能代替Android真机触屏验收。

## 已执行验证与复现

- 纯规则回归：`dotnet run --project Tools/CombatTests/CombatTests.csproj`。
- Unity共用输入及正常完整远征：`python -X utf8 Tools/Preparation/verify_combat.py`。
- 手机最新包：`python -X utf8 Tools/Mobile/mobile_test.py`；只构建可加 `--build-only`。
- 证据：`Reports/Combat/domain-tests.json`、`unity-verification.json`、`native-focus-verification.json`、实际GameView截图；手机状态单独保存在 `Reports/Mobile/combat-device-status.json`。

旧 `little-game.puzzle` 与 `little-game.structure` 扩展随旧客户端归档，不应再出现在当前发现结果中。

## 单位表现观测（扩展1.0.2）

`get_view.presentation.actors` 新增 active、action、actionTime，每个 renderer 返回 shaders、supported；upperArm 支持 TinyHero 的 upperarm_l 及旧骨架命名。正常回放结合多帧动作时间/骨骼姿态与截图检查，不将 animationPlaying 单个布尔值当动作正确的证明。

单位表现不改变正常操作参数或战斗规则；原16项原生工具仍可使用。`Tools/Preparation/verify_unit_presentation.py` 只读观察正常回放，`verify_unit_samples.py` 使用正常招募布阵到第六波并核查动作暂停/恢复。菜单 Build Unit Assets 仅用于资源加工，不属于正常玩法验收。

扩展1.0.3补充 `get_view.skin`（主题、已加载/缺失sprite）与按钮width/height（同样归一化到屏幕尺寸）。换肤仍使用原按钮ID与共用pointer；验证应覆盖准备、选兵、确认、战斗瞄准、奖励、胜负各阶段，检查按钮范围和重叠，不能沿用旧截图坐标。

## 3D 技能时间轴与单位 JSON 绑定（扩展 1.0.4 / `little-game.skill-timeline` 1.0.0）

- `little-game.combat` 1.0.4 在 `get_view.presentation.skillTimeline` 中新增运行时观测字段：`loadedBindings`、`loadedTimelines`、`valid`、`validationErrors`、`activeCount`、`totalTriggered` 与 `recentTriggered`。
- 新增专用扩展 `little-game.skill-timeline`（版本 `1.0.0`），用于读取、编排、校验、预览及热重载 `Assets/LittleGame/Resources/SkillTimelines/` 下的 `UnitSkillBindings.json` 与技能 JSON：
  - `list_timelines`：列出全部技能 JSON（含总时长、片段数）、单位绑定表与校验结果。
  - `get_timeline(timelineId)`：读取指定技能 JSON 及解析后的片段起止调度表。
  - `save_timeline(timeline)`（写操作）：经 `SkillTimelineSchedule` 校验无循环依赖与非法时间后落盘，并自动热重载到当前 Play 战斗。
  - `get_bindings` / `save_bindings(bindings)`（写操作）：读取或更新单位技能挂钩规则并校验所有 `TimelineId` 存在。
  - `validate_all`：一键校验全部绑定与技能 JSON。
  - `preview_timeline(timelineId, time, triggerInPlay)`：按指定时间点试算活跃片段；Play 模式下可选触发一次战场预览，不改写规则结算。
- 技能时间轴只负责 3D 动作、弹道、特效、受击反馈与轻震屏演出，严格保持单向读取，不参与或修改规则层伤害与胜负判定。

## 自由白框布阵、差异化移速、波后跑回归队与跟镜（扩展 1.3.0 / 快照 schemaVersion 4）

- `get_state` 升级至 `schemaVersion = 4`（`layout = "horizontal-free-deploy"`），新增 `deployBox`、`regrouping`，以及单位级 `homeX`、`homeZ`、`moveSpeed`、`facingYaw`、`turnSpeed`、`canMerge`。
- `deploy` 兼容原 `lane/row` 并新增自由世界坐标参数 `x/z`。
- `get_view` 新增 `presentation.cameraX` 与全局悬浮 `tip`（`text`、`isError`、`active`）；`get_definitions` 返回各职业 `moveSpeed`、`turnSpeed` 及 `deployBox`。

## RVO2 避障、自动升星、超界吸附与无面板拖拽交互（扩展 1.4.0 / 快照 schemaVersion 5）

- `get_state` 升级至 `schemaVersion = 5`（`layout = "horizontal-free-deploy-rvo"`），战斗移动与避障全面切换为纯规则层单线程确定性 ORCA/RVO2（参考 `E:\Project\AnimalBattle` 内 Apache 2.0 RVO2 Library C#）。
- 远程射程收紧（风弓手 4.85m、雷/冰法 4.15m），无目标时自动推进至射程内站定；12 波血量攻击与攻速统一在 `BattleDefinitions` 中平滑过渡。
- 凑齐三同自动合成升星（发出 `merged` 事件触发烟雾特效与金色大星级），去掉升二星强制选分支阻塞（`choose_branch` 仅作底层兼容保留）。
- `deploy` 传 `x/z` 超出白色布阵框不再拒绝，而是自动夹紧到可放置区域离手最近的点 A（拖拽未松手时在 A 点渲染半透明剪影预览）。
- `pointer` 入口支持：按下(tap)即刻选中单位且不弹出任何按钮面板、拖拽场上单位到 `get_view.reserveDropArea` 候补区直接召回、双击候补卡片自动寻找合适空位上阵（复用 `Deploy` 规则）。

## 战斗 AI 状态机与安全速度（扩展 1.5.0，兼容快照 schemaVersion 5）

- `get_state.units/enemies` 增加 `aiState`、`targetId`、`vx/vz`，状态为 `Idle/Pursuing/Engaging/Controlled/Dead`；`moveSpeed` 是最大速度，实际让行速度允许降低。
- `get_events` 增加 `ai_state_changed`，仅状态变化时发送，Source 为单位 ID、Target 为当前目标；波后复活直接重置 Idle。
- `get_view.presentation.actors` 增加实际表现坐标 `x/z/facingYaw`，结合规则位置和动作采样判断移动与动画是否一致。
- 模型同次同步的 `movementSpeed/motionPaused/forceWalk/hitStopped` 用于区分真实原地走路、受击顿帧和两次工具读取跨帧；不得把不同时刻的规则速度与动画直接判成不一致。
- 扩展 1.5.1 的 `get_view.presentation.actors[].groundMarkers` 返回实际存在的单位脚底提示（`name/active`）。移除职业/阵营、分支、三星及旧版手动合成装饰圈；选中标识和临时状态/技能范围仍按真实条件显示。正常操作参数和玩法规则未变。验证：`python -X utf8 Tools/Preparation/verify_ground_markers.py`。
- `get_definitions.rules` 返回 `ai=finite_state_machine`、`aiStates`、`stickyCombatTargets`、`avoidancePreservesSafeVelocity`。
- 正常招募/开战/集火/保护/重开参数不变，共用玩家入口未增加 AI 调试捷径。完整回放和移动采样：`python -X utf8 Tools/Preparation/verify_combat_movement.py`，输出 `Reports/Combat/movement-fsm-20261125/`。

## 技能规则时序与二维预览（`little-game.skill-timeline` 2.0.0）

- 技能时间轴 JSON 升级至 `SchemaVersion = 2`，新增纯规则时序配置 `Execution`（`Enabled`、`LaunchKey`、`ImpactKey`、`RecoveryKey`、`InterruptOnControl`、`TrackTarget`）与空间字段（`TargetGround`/`CastPoint`/`ImpactPoint` 挂点、`LocalOffset`、`OrientToCaster`）。启动与热重载时通过 `BattleSession.ConfigureSkills` 下发给规则层，由规则层按统一节点推进前摇/发射/命中/后摇与中断结算，不依赖 Unity 动画回调。
- `get_state` 新增 `skills` 列表，暴露活跃与近期技能实例（`id`、`source`、`target`、`trigger`、`timelineId`、`phase`、`startedAt`、`launchAt`、`impactAt`、`endAt`、`targetX/Z`、`facingYaw`、`cancelReason`）；`get_events` 新增 `cast_started`、`cast_launched`、`cast_impact`、`cast_completed`、`cast_cancelled` 事件；`get_view.presentation.skillTimeline` 新增 `instances` 演出实例观测。
- `little-game.skill-timeline` 2.0.0 变更：
  - `get_timeline(timelineId)` 补充返回 `ruleTiming`（解析后的规则层发射/命中/后摇时间与中断配置）。
  - 新增 `configure_preview(timelineId, distance, angle, targetSpeed, targetCount, time)`（仅 Edit 模式可用）：配置二维距离、朝向角、移动目标速度与多目标数量进行演出预览（`previewOnly=true`，不替代正常战斗验收）。
  - 新增 `runtime_instances`（Play 模式只读）：直接查询规则层 `SkillInstances` 的阶段、时间节点与中断原因。
  - `validate_all` 补充校验特效资源（`Resources/SkillEffects/*`）、动作名合法性及 `Execution` 规则节点时序。


## A*导航与阻挡等待（扩展1.6.0，快照schemaVersion 6，历史实现）

> 2026-11-27 更新：Roy-T.AStar 已归档至 `Archive~/NavigationRoy`，由下方原生 `NavMeshAgent` 章节覆盖。

`get_state.layout`曾为`horizontal-free-deploy-astar`，单位和敌人新增`navigation`，含`status`（Holding/Moving/Waiting/NoPath/Arrived）、`blockReason`、`blockedFor`、`goalX/goalZ`、`revision`、`remainingCorners`和`corners`。FSM增加Waiting；`vx/vz`均为碰撞/边界处理后的实际位移速度。

## Unity 原生 NavMeshAgent 导航（扩展 2.4.0，快照 schemaVersion 8，当前实现）

本节覆盖前述 A* 导航描述。实战采用唯一导航后端 `unity_navmesh_agent_attack_slots`（Unity 原生 `NavMeshAgent` + `HighQualityObstacleAvoidance` 局部避障 + 规则层攻击位预约与站定切洞）：

- `get_state.layout` 为 `horizontal-free-deploy-navmesh`，`schemaVersion = 8`；单位/敌人沿用 `navigation` 结构，明确区分 `Waiting`（`attack_slot_unavailable` / `native_avoidance_wait` / `navmesh_rebinding`）与 `NoPath`（`path_blocked`）。
- `get_definitions.rules` 返回 `navigation = "unity_navmesh_agent_attack_slots"`、`avoidance = "unity_navmesh_agent_attack_slots"`、`rvoActive = true`、`blockedWait = true`、`actualDisplacementVelocity = true`。
- `get_navigation(unitId?)` 保留原有 `algorithm`、`rvoActive`、`fixedStep`、`cpuTotalMs`、`units`、`enemies` 字段，并新增 `native` 诊断对象（返回原生代理数、对象池数、`pathRequests`、`failedPaths`、`buildMs`、`nativeAvoidance`、`meshTypes` 及各代理绑定/切洞/路径状态）。
- 原生 `unity_combat_*` 直接工具当前共 13 项（旧战术指挥 `aim/focus/protect` 及 `choose_branch` 已随玩法改造移除）。
- 隔离几何/状态/18&50 单位压测菜单：`Little Game/Combat/Verify Native Navigation`（输出 `Reports/Combat/navmesh-20261127/validation-latest.json`）；正常玩法及动画一致性回归：`python -X utf8 Tools/Preparation/verify_combat_movement.py`；十波无限帧率采样：`python -X utf8 Tools/Preparation/profile_combat.py <label>`。验收报告见 `Reports/Combat/navmesh-20261127/README.md`；编辑器数据不可写成真机表现。
