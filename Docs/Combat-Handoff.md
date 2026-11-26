# 军团客户端本轮交接

> 2026-11-25 更新：已接替并继续 Demo 技术栈迁移，当前分层、启动、资源、技能编辑器与验收边界以 [Framework-Migration.md](Framework-Migration.md) 为准。下文关于“尚未迁入完整 TimelineEditor”、旧 little-game.skill-timeline 扩展和 Resources 技能镜像的描述属于历史状态；当前使用原生 17 个 unity_skill_editor_* 工具、标准 TimelineConfig 与共享 3D 播放器。战斗扩展为 1.7.0，规则状态 schema6。

## 当前成果

当前可运行客户端由纯规则、应用宿主、三维表现、输入、平台接口、启动与编辑器适配组成，默认入口为 `Assets/Scenes/Combat.unity`。在 Unity 使用菜单 `Little Game/Combat/Open Combat` 后点击 Play，可直接招募、部署与战斗，不需配置角色或关卡。

已经实现并验证的流程：横向三同转盘、每波次数、两空保底、容量暂存、部署与交换、三兵合成、二星职业分支、普通波分栏和相邻远程支援、首领撤栏和近战接敌、冻结免疫转减速、一次集火或保护、奖励、12波胜负、失败及重开、波前检查点与可复现随机。

用户界面新局产生新种子；MCP可以显式给固定种子复现。正常输入与MCP共用同一套指令校验，没有工具直接改血、生成胜利或跳波。

## Demo 复用与归档

- 按用户指定从 `F:/Git/kabu_mobile/demo` 直接迁入 PureMVC 和 GameDebug，并同步本工程内的 MCP 编辑器与服务端源码。没有通过子模块或修改 PackageCache 接入。
- 参考 Demo 的运行时、上下文、视图和流程分离方式。本游戏是本地即时战斗，未把 Demo 的回合制服务端协议和结算强行迁入。
- `Archive~/TeardownClient` 保留旧代码、场景、MCP和专用验证工具；`Archive~/McpPrior` 保留同步前的差异文件。旧玩法不参与当前启动、构建和工具发现。
- 来源和许可见 `Assets/LittleGame/ThirdParty/SOURCES.md`。已迁入 KayKit 的四名角色、贴图、基础与战斗动作，用实际骨架播放状态和画面核查。首领目前使用放大的免费角色占位，不是最终石甲巨像资产。

## 验证证据

| 项目 | 证据与范围 |
| --- | --- |
| 纯规则 | `Reports/Combat/domain-tests.json`：33项检查，含固定步长、确定性、容量、免疫和3个种子的正常策略12波胜利；不是数值平衡或真人留存结论 |
| Unity与MCP | `Reports/Combat/unity-verification.json`：正确工程、16个原生工具、23项扩展操作、玩家共用pointer、完整远征胜利、漏怪失败、重开；运行Console四类均0条 |
| 集火 | `Reports/Combat/native-focus-verification.json`：真实存活敌人、正常瞄准与确认、本波战术次数消耗 |
| 最新UI与动作 | `Reports/Combat/final-ui-verification.json`：取消重开不修改种子，确认重开创建新种子，实际骨架动画在推进；未把窗口内模型存在当成动作通过 |
| 画面 | `Reports/Combat/combat-preparation.png`、`combat-fighting.png`、`combat-boss-warning-6.png`、`combat-victory.png`、`combat-defeat.png` 为实际GameView帧，不是手机截图 |
| Android | `Library/MobileTesting/build-status.json` 是最新构建任务状态；手机连接状态独立记录于 `Reports/Mobile/combat-device-status.json`，未连接设备不代表安装或触屏通过 |

当前数值偏向完整流程验证，自动策略在三个种子中可以获胜，不说明难度、组合平衡或商业吸引力成立。Android Development APK 使用 IL2CPP ARM64；开发包保留旧标识以原位更新和保留应用数据，展示名为符阵军团，正式发行标识未定。

## 尚未完成的产品能力

已接入九类单位样品、作者原武器挂点、基础动作及Demo卡通渲染；最终逐星套装、完整命中时序演出、更多职业、进阶内容、真人试玩和商业化未完成。IAP仅保留接口边界，不提供购买，也没有接广告、账号或线上运营服务。

Demo 的完整 TimelineEditor 尚未迁入本项目。已核对其依赖为 UnitCore、Spine、DOTween、Rendering/Optimize；现有播放器以 Spine 动作能力为主，3D单位虽有变换适配，仍需具体动作播放验证。因此不能把 Demo 内已有技能编辑器 MCP 描述成本项目已经能用它生产3D技能。当前战斗技能由独立规则和表现模块实现。

后续接入顺序：核对正在完善的Demo版本 → 直接迁入所需编辑器和共用配置/播放器依赖 → 绑定本项目3D角色与特效资源路径 → 校验规则命中事件与表现时序的单向关系 → 用技能编辑器MCP完成目录、试算、应用、保存重载与实战对照。源工程不在本轮修改。

2026-11-24本轮曾构建并更新安装一次单位测试包，但前台被其他应用占用，未通过手机验收；随后用户明确只需Unity验证、不用装手机，因此停止后续手机操作。该APK早于最新UI与地面环改造，不得称为最新版或复用交付。后续用户明确要求手机交付时运行 `python -X utf8 Tools/Mobile/mobile_test.py` 默认重新构建、安装和启动，工具不卸载或清空应用数据。

## 继续工作入口

架构与已确认规则见 `Combat-Architecture.md`，工具契约见 `Combat-MCP.md`。`Design/2026-11-23-combat-review.*` 为早期方案，不覆盖后来确认并实现的横线招募和三兵升星。

没有提交、暂存或推送代码。

## 2026-11-24 单位与整体界面改造

- 来源统一维护在 Art-Resource-Catalog.md，新增资源参考与授权自动写入根AGENTS.md。PELibDemo提供TinyHero、狼及Fantasy RPG UI；AutoClash提供石头人；Demo提供Roco卡通Shader与配套描边。外部源工程保持只读。
- 九类单位已完成精简装配、基础动作和骨架路径验证；移除原占位圆盘，使用阵营细环减少遮挡。星级仍使用既有大小与星标，逐星独立套装尚未制作。
- UI已统一为靛紫/金色奇幻皮肤：波次进度、城门、三路敌情、图标转盘、候补卡片、招募/开战、战术、分支/合成、奖励及确认弹窗。按钮ID与正常规则不变；按压反馈由玩家共用pointer驱动。
- 最新 `Reports/Combat/unity-verification.json` 包含九组回归结论与多阶段UI边界、触屏高度、按钮无重叠、缺失sprite检查，以及正常12波胜利、少兵失败和重开；Console四类均0。图片是Unity GameView帧。角色只读回放采样见 `Reports/Art/unit-runtime-verification.json`。
- 本轮按用户最新要求仅进行Unity验收。未验证最新UI的手机性能、手机布局与真实触屏，不复用早期APK代表最新源码。
