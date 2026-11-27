# Unity 原生 NavMeshAgent 战斗导航替换与验收报告（2026-11-27）

## 结论与架构分工

实战导航后端已切换为**单一后端**：`unity_navmesh_agent_attack_slots`（[BattleNavMeshNavigation.cs](../../../Assets/LittleGame/Client/Battle/Navigation/BattleNavMeshNavigation.cs)）。旧 Roy-T.AStar 导航已归档至 `Archive~/NavigationRoy`，不参与实战运行。

职责边界：
- **规则层 FSM（`BattleAgentBrain` + `BattleSession`）**：决定目标选择、追击/交战/受控/等待/死亡状态、刺客跳后排、暂停与倍速；不直接依赖 Unity 组件，仅通过 `IBattleNavigation` 发送意图并回写实际位移与速度。
- **攻击位预约与可达回退（`BattleNavMeshNavigation.Plan`）**：围绕目标按 `24` 角度无 GC 筛选合法空位（校验实际命中点落入 `AttackReach - 0.045f` 射程容差内），对最近前 `8` 个候选位按序调用 `CalculatePath`，首个 `PathComplete` 生效；区分无空位（`Waiting / attack_slot_unavailable`）与通路完全封死（`NoPath / path_blocked`）。
- **引擎原生导航与局部避障**：启动时按 5 种单位半径（`0.25 / 0.4 / 0.5 / 0.65 / 1.15`）构建平面 `NavMeshData`（一次性构建约 `4.9 ms`），开启原生 `HighQualityObstacleAvoidance`（RVO）处理移动单位错身让行；仅连续站定 `>= 0.65s` 的单位切为 `NavMeshObstacle` 切洞，撤销切洞后等待 1 帧再重绑 `NavMeshAgent`，防止自身旧洞弹偏；单帧多逻辑步（如 2 倍速/低帧）共享帧内位移终点，防止虚假零速度与 `Waiting` 闪烁。

---

## 一、9 项隔离几何 / 状态 / 压力验收

测试入口：`Little Game/Combat/Verify Native Navigation`（[NavMeshValidation.cs](../../../Assets/LittleGame/Client/Editor/NavMeshValidation.cs)），原始证据见 [validation-latest.json](validation-latest.json)。平台：`WindowsEditor`，Console 0 Error / 0 Exception / 0 Warning。

| 场景 | 状态 | 关键实测指标 |
|---|---|---|
| `stationary_wall_detour` | 通过 | 绕行抵达 `x=1.23`，最大绕行 `maxAbsZ=2.33`，3 名站定单位位移为 `0` |
| `blocked_then_release` | 通过 | 封死后于 `x=-1.56` 停步进入 `NoPath/Waiting`；撤销中间阻挡后恢复通行至 `x=1.05` |
| `u_shape_detour` | 通过 | 从 U 形口袋绕出抵达 `x=1.26`（`maxAbsZ=2.02`） |
| `opposing` | 通过 | 双单位相向错身通过，全程最小中心距 `0.844m >= 0.74m`（零互穿） |
| `freeze_pause_resume` | 通过 | 冻结绝对站定（`<0.001m`），解冻切洞撤销并恢复移动，暂停零位移且继续衔接正常 |
| `body_size` | 通过 | 首领半径 `1.15m` 无法通过窄道，常规半径 `0.25m` 顺利通过同一窄道 |
| `boss_surround` | 通过 | 6 名近战绕开已站定友军并全部进入 Boss 射程（`dist <= 2.08m`） |
| `attack_slots_full` | 通过 | 6 名站定单位零推移，多余近战明确 `Waiting / attack_slot_unavailable` |
| `benchmark_18_and_50` | 通过 | 见下表（Editor 无限帧率，含 `NavMeshManager` 原生采样） |

### 隔离规模基准（WindowsEditor 无限帧率，`nativeRecorderValid = true`）

| 规模 | 帧数 | 一次性建网 | 适配层 P99 / Max | 原生 `NavMeshManager` P99 / Max | 全流程 P50 / P95 / P99 / Max | 路径请求 / 失败 |
|---|---:|---:|---:|---:|---:|---:|
| **18 单位（9v9 实战上限）** | 486 | 4.911 ms | 0.792 ms / 1.425 ms | 0.235 ms / 1.030 ms | **0.119 / 0.252 / 0.717 / 1.428 ms** | 107 / 0 |
| **50 单位（25v25 压力）** | 488 | 4.235 ms | 3.561 ms / 6.403 ms | 0.285 ms / 0.472 ms | **0.159 / 0.444 / 3.046 / 6.406 ms** | 379 / 339 |

- 18 单位隔离场景满足暂定工程目标（全流程 P99 `0.717 ms <= 1.0 ms`，重规划峰值 `1.428 ms <= 2.0 ms`）。

---

## 二、纯规则回归 & 完整 10 波正常玩法与动画一致性验收

1. **纯规则回归**（`dotnet run --project Tools/CombatTests/CombatTests.csproj` 及 `--movement`）：
   - 全部规则、存档恢复、技能时序、羁绊、法力、FSM 状态边界及 3 组种子（`173 / 7 / 91`）10 波远征全部通过（使用纯规则测试夹具 `TestHarnessNavigationStub`，不替代 Unity 原生导航验收）。
2. **Unity 完整玩法 + 共用输入 + 移动动画观测**（`python -X utf8 Tools/Preparation/verify_combat_movement.py`）：
   - 证据：[unity-verification.json](unity-verification.json)、[movement-observations.json](movement-observations.json) 及同目录 13 张 540×960 GameView 截图。
   - 全部 13 项原生 `unity_combat_*` 工具、`get_navigation` 返回 `unity_navmesh_agent_attack_slots`、`rvoActive=true`、共用 `pointer`（点击/双击上场/超界吸附/拖回召回/取消）、10 波从零单位正常远征获胜、单兵团灭判负与重开归零、UI 边界无重叠及 Console 0 Error / 0 Exception / 0 Assert / 0 Warning 均通过。
   - 361 帧战斗采样：`stationaryWalkSamples = 0`（无原地走路）、`frozenWalkSamples = 0`、`waitingSamples = 117`（等待帧均严格满足 `vx == 0 && vz == 0`）、10 名单位在等待后真实恢复移动、`multiCornerSamples = 94`。

---

## 三、十波无限帧率真实战斗性能对比

采样条件（与两次旧 Roy 基线完全一致）：`WindowsEditor`、`540×960`、`targetFrameRate = -1`、`vSyncCount = 0`、`seed = 173`、`speed = 2`、`sameFormations = true`（十波阵容完全一致）。原始数据位于 `Reports/Performance/20261127/navmesh-20261127.json` 与 `comparison.json`。

### 总体对比（战斗帧）

| 指标 | 初始 Roy A* (`before-repeat`) | 优化后 Roy A* (`after-verified`) | **Unity 原生 `NavMeshAgent` (`navmesh-20261127`)** |
|---|---:|---:|---:|
| 战斗总帧数 | 8,873 | 9,221 | 7,499 |
| `LittleGame.Rules` Max | 158.421 ms | 19.254 ms | **0.794 ms**（-95.9% vs 优化 Roy / -99.5% vs 初始） |
| 导航适配层 Bridge P99 / Max | — | — / 19.145 ms | **0.155 ms / 0.627 ms** |
| 原生 `NavMeshManager` P99 / 第2~10波Max | — | — | **0.226 ms / 0.488 ms**（第1波首帧切洞初始化 2.882 ms） |
| **导航全流程（Bridge+Native）P50 / P95 / P99** | — | — | **0.088 ms / 0.186 ms / 0.292 ms** |
| **第 2~10 波导航全流程 Max** | — | 19.145 ms | **0.677 ms** |
| `Crowd.Avoidance` / `Carving.ApplyResults` 全十波 Max | — | — | **0.215 ms / 0.316 ms** |
| 稳态导航 GC（Wave 5 / 8 / 10 Hierarchy） | — | — | **0 B**（`NavMeshManager` 与 `LittleGame.Rules` 均 `0 B`） |
| 主线程超 16.67 ms 帧数 | 74 | 47 | **19** |

### 逐波峰值明细（ms）

| 波次 | 旧初始 Rules Max | 优化 Roy Nav Max | 新 Rules Max | 新 Bridge Max | 新 Native Max | 新导航全流程 P99 | 新导航全流程 Max |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 0.148 | 3.744 | 0.431 | 0.374 | 2.882* | 0.219 | 2.910* |
| 2 | 7.785 | 8.457 | 0.519 | 0.419 | 0.286 | 0.245 | 0.422 |
| 3 | 7.792 | 8.642 | 0.479 | 0.407 | 0.244 | 0.234 | 0.411 |
| 4 | 9.278 | 10.014 | 0.558 | 0.467 | 0.362 | 0.237 | 0.470 |
| 5 (Boss) | 34.592 | 6.422 | 0.409 | 0.322 | 0.247 | 0.209 | 0.326 |
| 6 | 39.551 | 10.095 | 0.718 | 0.579 | 0.317 | 0.301 | 0.582 |
| 7 | 37.934 | 13.783 | 0.731 | 0.627 | 0.488 | 0.292 | 0.631 |
| 8 | 158.421 | 17.310 | 0.794 | 0.593 | 0.451 | 0.334 | 0.677 |
| 9 | 72.644 | 19.145 | 0.617 | 0.526 | 0.445 | 0.319 | 0.530 |
| 10 (Boss) | 35.036 | 7.525 | 0.463 | 0.371 | 0.295 | 0.253 | 0.374 |

\* 注：第 1 波第 238 战斗帧存在单次 `nativeNavigationMs = 2.882 ms`（首次运行时启用 `NavMeshObstacle` 切洞分配 Detour 瓦片缓存的一次性冷启动开销），该波其余帧均 `< 0.45 ms`，第 2~10 波全程未再出现 > 0.68 ms 尖峰。

---

## 限制与未验证边界

- 以上全部采样与验收均在 **Windows Unity Editor**（Profiler 开启、非 Deep Profile）下完成，**不得作为 Android 真机帧率或最终手机性能结论**。
- 本轮未执行手机 APK 构建与真机复测；后续手机交付时按项目流程用真机复核导航、渲染与整帧耗时。
- 采样期间 Unity Profiler 环缓冲录制 14,430 帧曾触发编辑器内存告警（非游戏业务报错，已在采样后清空 Profiler 帧缓冲）；正常玩法回归（`verify_combat_movement.py`）Console 0 Error / 0 Exception / 0 Assert / 0 Warning。
