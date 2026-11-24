# 直接迁入来源

日期 2026-11-23。所有代码为本工程内可修改副本，不使用 Demo Git 子模块或仓库引用。

| 内容 | 本地来源 | 处理 |
| --- | --- | --- |
| PureMVC C# Multicore | `F:/Git/kabu_mobile/demo/client-demo/Assets/Scripts/HotUpdate_Main/Core/PureMVC` | 原样复制，保留所有源码版权头；CC Attribution 3.0，原作者 Saad Shams。许可 https://creativecommons.org/licenses/by/3.0/ ，项目来源 https://github.com/PureMVC/puremvc-csharp-multicore-framework 。未修改框架代码；单独增加程序集定义 |
| GameDebug | `F:/Git/kabu_mobile/demo/client-demo/Assets/Scripts/AOT/GameDebug/GameDebug.cs` | 用户指定自有技术来源，直接迁入统一业务日志 |
| Unity MCP | `F:/Git/kabu_mobile/demo/kabu-unity-mcp` | 在既有项目副本上按文件哈希同步，改前文件备份到 `Archive~/McpPrior`，变更清单在 `Reports/Combat/source-migration.json`；本项目新增 CombatTools 和目录注册 |
| KayKit Adventurers Free 2.0 | `E:/Art/material-vault/libraries/kaykit/downloads/adventurers-free-2.0.zip` | 只提取 Knight/Ranger/Mage/Barbarian、对应贴图与 Rig Medium 动画，包内 CC0 License.txt 随资源保留。官方 https://kaylousberg.itch.io/kaykit-adventurers 。未修改源模型，导入Legacy动画并运行时缩放、绑定URP材质 |
| KayKit Character Animations Free 1.1 | `E:/Art/material-vault/libraries/kaykit/downloads/character-animations-free-1.1.zip` | 只提取 Medium CombatMelee/CombatRanged，包内 CC0 许可保存为 `ANIMATIONS-LICENSE.txt`；原始动作未修改，客户端选择动作并调整播放速度 |

Domain 与新客户端适配是本项目代码，架构参考 Demo 的运行时、流程与表现分离；不是把 Demo 回合制服务端结算复制成本地即时结算。未迁入付费业务、网络服务端、YooAsset、HybridCLR 等当前不需要的业务依赖。

## 2026-11-24 单位与卡通渲染

- 用户明确授权 PELibDemo 模型与 AutoClash 资源可用，以及 Demo 单位卡通 Shader 可用，不重复请求商用确认。具体源路径与范围见 `Docs/Art-Resource-Catalog.md`。
- `Tools/Preparation/import_unit_samples.py` 提取 TinyHero 装配模型/武器与少量基础动作、Cute Series 狼、AutoClash 石头人，源工程只读。源/目标文件哈希保存在 `Reports/Art/unit-source-migration.json`。
- 模型与贴图位于 `Assets/LittleGame/Art/UnitSources`，精简生成物及独立动作位于 UnitGenerated；Resources/UnitAppearances 与 UnitModels 为游戏实际引用。剔除源演示控制器、闲置装配、碰撞及动作事件；动作只驱动表现。
- Roco Shader 三文件来自 Demo client-demo-assets/Assets/Art/Shaders/Roco，描边 Feature 来自 client-demo/Assets/Scripts/client-shared/Rendering/Runtime/Outline。移除 Demo 专用材质GUI和配置类依赖，修正正交镜头的描边线宽换算；当前未使用ID染色图，关闭该依赖。
- KayKit 保留为旧样品/对照，当前战斗九类角色已切换到新配置；尚未制作逐星专属套装和完整技能命中时序。

- UI：用户2026-11-24明确授权PELibDemo Art内UI用于整体改造。本项目提取Fantasy RPG套件21张UI原图，保留九宫格边距，未迁入其演示脚本或业务预制体。来源/哈希见 Reports/Art/ui-source-migration.json；工具 import_combat_ui.py 可复现。
