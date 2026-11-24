# 当前客户端入口

2026-11-24 场景已更新为使用 AutoClash 模型与 Demo URP Shader 的遗迹庭院，资源取舍、MCP观测及验证见 [Courtyard-Scene.md](Courtyard-Scene.md)。

当前方向为海外竖屏 3D 军团手游，IAP 尚未接入；历史拆解原型退出当前客户端。最新用户认可规则、目录、迁入取舍和启动流程见 [Combat-Architecture.md](Combat-Architecture.md)。

- Unity 打开 `Assets/Scenes/Combat.unity`，或菜单 `Little Game/Combat/Open Combat`，点击 Play 直接进入远征。
- `Client/Domain` 是纯规则；`Application` 是统一指令和通知入口；`Presentation`、`Input`、`Platform` 与 Editor MCP 分离。
- MCP：`http://127.0.0.1:3833/mcp`；专用工具前缀 `unity_combat_`，扩展为 `little-game.combat`。先确认 `dataPath` 为本工程。
- 源码回归：`dotnet run --project Tools/CombatTests/CombatTests.csproj`（本机 .NET 10）。
- Unity 正常玩法与共用输入回归：`python -X utf8 Tools/Preparation/verify_combat.py`。
- 手机最新版：`python -X utf8 Tools/Mobile/mobile_test.py`；仅构建加 `--build-only`，不连接手机时不得声称已安装或真机通过。

`Design/2026-11-23-combat-review.*` 是讨论前期稿，按列出兵、单次招募和传承等已被后续聊天方案替代，不能作为当前规则来源。旧 `Prototype.md`、`Structure-Sample.md` 为历史记录；旧客户端与专用工具保存在 `Archive~/TeardownClient`。

单位现已接入 PELibDemo TinyHero 四职业及人形敌人、Cute Series 狼、AutoClash 石甲巨像，并使用 Demo Roco 卡通 Shader；来源、装配、验证边界见 [Art-Resource-Catalog.md](Art-Resource-Catalog.md)。界面已使用PELibDemo Fantasy RPG素材统一改造，含图标转盘、候补卡片、波次/城门/敌情、战术、奖励与确认弹窗。当前仍为单位样品；没有完成最终技能演出、商业化或市场验证。Demo 的完整 TimelineEditor 仍需根据 3D 角色播放支持与 Spine、DOTween、Rendering/Optimize 依赖迁入，不把窗口或MCP接口存在误写为已接入本项目。

本轮用户明确只需Unity验证，不需要手机构建安装。最新UI流程与输入回归结果见 `Reports/Combat/unity-verification.json`，首屏与首领实际帧见 `Reports/Art/ui-redesign-preparation.png`、`Reports/Art/units-boss-final.png`。
