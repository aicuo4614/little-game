# 战斗 MCP 操作与验证

端点 `http://127.0.0.1:3833/mcp`，工程 `E:/Project/little-game/Assets`。所有写操作均走 `BattleApplication.Submit → BattleSession.Execute`，与玩家相同，不存在跳波、直接改血或直接获胜的生产工具。

## 发现与入口

1. `unity_get_project_info` 确认工程路径，`unity_get_compilation_status` 确认编译。
2. 退出 Play 后执行菜单 `Little Game/Combat/Open Combat`，再 `unity_enter_play_mode`。
3. `tools/list` 应发现 16 个 `unity_combat_*` 工具。`unity_extension_catalog(extensionId="little-game.combat")` 另提供完整 23 项扩展操作及参数。
4. `unity_combat_get_state` 读取本波、敌情、军团、转盘、可用操作、预警和事件；`unity_combat_get_view` 读取真实屏幕目标。

## 正常一局

| 操作 | 输入 | 条件与结果 |
| --- | --- | --- |
| `unity_combat_restart` | seed，可省略为173 | 正常重开，不保留本局军团与奖励；相同种子复现 |
| `unity_combat_spin` | 无 | 准备阶段、有次数、无暂存；十字中线（中横/中竖）三同出兵。两次空转后的下一次保底 |
| `unity_combat_deploy` | unitId、lane、row | lane/row均0到2；未满人口时候补可上空格，满人口仅允许与已占格替换或调整已上阵位置 |
| `unity_combat_merge` | unitId、otherId、thirdId | 三个同职业同星不同单位；保留宿主位置、分支，消耗另外两名 |
| `unity_combat_choose_branch` | unitId、branch | 二星必须选合法分支，选择后锁定 |
| `unity_combat_start_wave` | 无 | 暂存清空、分支已选、至少一个上阵；自动战斗，剩余转动放弃 |
| `unity_combat_aim` | kind=Focus或Protect | 暂停瞄准，不扣次数 |
| `unity_combat_focus` | unitId为敌人ID | 存活目标；消耗本波唯一战术，射程不改变 |
| `unity_combat_protect` | lane | 该路有存活友军；4秒25%护盾；空路拒绝不扣次数 |
| `unity_combat_choose_reward` | reward | 第4/8波后，选择未持有的 Reinforcements/WideStorm/LongFreeze |
| `unity_combat_next_wave` | 无 | 仅波后，无权跳过战斗或奖励；满血、刷新转盘 |

盾分支 Bastion/WarCry；弓 Pierce/Gale；雷 StormPool/ChainLightning；冰 DeepFreeze/IceArmor。

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
- `deploy` 兼容原 `lane/row` 并新增自由世界坐标参数 `x/z`；超出整体白色布阵框范围返回规则拒绝并弹出红色悬浮 Tip。
- `get_view` 新增 `presentation.cameraX` 与全局悬浮 `tip`（`text`、`isError`、`active`）；`get_definitions` 返回各职业 `moveSpeed`、`turnSpeed` 及 `deployBox`。
