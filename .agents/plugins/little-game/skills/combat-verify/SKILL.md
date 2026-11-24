---
name: combat-verify
description: >-
  Run end-to-end verification for little-game combat rules, Unity compilation,
  normal gameplay via 3833 MCP tools, shared pointer input, UI layout, and Console logs.
  Use after modifying gameplay rules, combat UI, unit presentation, input, or combat MCP tools.
---

# little-game 战斗与 MCP 回归验证

执行本工程的完整验收流程，确保规则、表现、共用输入与 MCP 一致。详细工具契约见 `Docs/Combat-MCP.md`。

## 前置核对

1. **确认连接目标**：调用 `unity_get_project_info`（使用 `little-game` 3833 端口服务，切勿误用 8080 的其他工程），确认 `dataPath` 为 `E:/Project/little-game/Assets`。
2. **确认编译状态**：调用 `unity_get_compilation_status`，确保无编译错误且 `isCompiling=false`。

## 标准验收步骤

1. **纯规则回归（无 Unity 依赖）**
   ```powershell
   dotnet run --project Tools/CombatTests/CombatTests.csproj
   ```
   验证结果会输出到 `Reports/Combat/domain-tests.json`。

2. **场景与 Play 模式准备**
   - 若正在 Play，先 `unity_exit_play_mode`。
   - 执行菜单 `Little Game/Combat/Open Combat` 打开 `Assets/Scenes/Combat.unity`。
   - 调用 `unity_enter_play_mode` 进入运行态。

3. **正常玩法与共用输入回归**
   - 完整自动化回归（覆盖 16 个原生 `unity_combat_*` 工具、23 项扩展操作、共用 `unity_combat_pointer` 点击/拖拽/取消、多阶段 UI 边界与无重叠检查、12 波胜利/漏怪失败/重开）：
     ```powershell
     python -X utf8 Tools/Preparation/verify_combat.py
     ```
   - 涉及单位模型、动作或渲染改动时，补充运行：
     ```powershell
     python -X utf8 Tools/Preparation/verify_unit_presentation.py
     python -X utf8 Tools/Preparation/verify_unit_samples.py
     ```
   - 若需针对单点交互/截图人工复核：使用 `unity_combat_get_state`、`unity_combat_get_view`、`unity_combat_pointer` 和 `unity_extension_call(extensionId="little-game.combat", operation="capture_view", ...)`（截图在下一帧写入，需等待文件落盘后再 `read_file` 检查）。

4. **Console 零报错核查与收尾**
   - 调用 `unity_get_console_logs` 分别检查 `Error`、`Exception`、`Assert`（均须为 0 条），并关注 `Warning`。
   - 调用 `unity_exit_play_mode` 退出运行态，确认 `unity_get_play_mode_state` 回到 `Stopped`。

## 约束

- 玩家输入与 MCP 共用 `BattleApplication.Submit → BattleSession.Execute`，调试手段不能代替正常规则验收。
- 编辑器性能与 GameView 截图不得写成 Android 真机数据。
- 若本次修改改变了玩法规则或 UI 交互，必须同步核查并更新受影响的 `unity_combat_*` 工具与 `Docs/Combat-MCP.md`。
