# Demo 客户端技术栈迁移（2026-11-25）

本次接替额度中断的工作，来源工程 `F:/Git/kabu_mobile/demo` 始终只读。目标为单 Unity 工程、UGUI、3D Animator，保留现有纯规则战斗；未接入 MMO 网络/登录、Spine 或 FairyGUI，不改变当前商业化状态。

## 实际分层与启动

- `Assets/Scripts/AOT`：MyApplication、启动配置、YooAsset ResourceMgr、CodePackageHotUpdateFlow、HybridCLR HotUpdateMgr、协程宿主及 UGUI 根。
- `Assets/Scripts/Shared/HotUpdate_UnitCore`：标准 TimelineConfig、Clip、UnitMono、3D Animator 采样及共享 TimelinePlayback。
- `Assets/LittleGame/Client/Domain`：LittleGame.Domain，纯 C# 规则、技能时序、FSM 与 A*；独立于 Unity 和 MCP，打包时也是热更程序集。
- `Assets/Scripts/HotUpdate_Main`：MVC/MainFacade、七个 Service、Mgr、Pool、模块窗口面板生命周期及画质配置。
- `Assets/LittleGame/Client`：LittleGame.Client 热更业务程序集，BattleModule → BattleView 的九个控制器 → BattleUnit 的七个控制器和表现状态机，输入仍经 BattleApplication.Submit → BattleSession.Execute。
- `Assets/Scripts/Editor/TimelineEditor`：Demo 窗口及完整 MCP Host；旧 JSON 编辑器和旧角色/场景播放器不参与运行。

Play 启动由 AOT 发起，激活 LaunchPackage → CodePackage 检查/下载/加载 → MVC 初始化 → 五个内容包检查/下载/激活 → GfxQualityMgr 读 SO → 反射创建 ClientBootstrap → ModuleMgr 打开 BattleModule。资源失败时停止启动，不再跳过热更直接拉起业务。Editor 默认 EditorSimulateMode；EditorSimulateDevice 走构建包与本地 CDN，DLL 仍使用 Editor 编译程序集以避免类型重复。

## 资源与构建

包为 LaunchPackage、CodePackage、MainPackage、ModulePackage、UnitPackage、EffectPackage、ScenePackage；实际地址由本工程的 AddressByBundleAndRelativePath 生成。

- 单位：`Unit/Low/Archer/Archer` 等；特效：`Effect/Low/fx_arrow_basic` 等。
- 技能：`Timeline/archer_basic` 等；唯一源文件为 `Assets/Package_Unit/Timeline/Base`，绑定表为 `Skill/UnitSkillBindings`。
- 画质：ModulePackage 的 `SOConfig/QualitySOConfig`，配置加载通过 SOConfigService，画质档位通过 LocalSaveService 持久化。
- 对象池通过 ResourceService 加载地址，每个 prefab 池持有一个资源引用；战斗结束释放实例和引用。归还重置粒子，重新取出角色重绑 Animator。
- 原 Resources 技能镜像和旧生成器保存在 `Archive~/MigrationRedundant`，不再参与构建；编辑器保存的标准 JSON 直接供实战读取。

菜单 `Little Game/Framework/Configure Stack` 幂等配置七个收集器、四个 HybridCLR 热更程序集及默认画质/启动 SO。`Compile Hot DLLs` 真实调用 HybridCLR 编译器并同步四个 DLL。迁入的 `HybridCLR/编译热更 DLL 并同步到 Package_Code` 才是包含 GenerateAll 和 AOT 元数据的完整发布准备流程；当前编译 DLL 的检查不代替该流程或原生运行验证。

`Build Resource Packages` 调用迁入并适配为七包的 YooAsset SBP 构建器。全部包复制到 StreamingAssets 作为内置首包，输出到 Bundles，再同步到本工程 `Library/LocalCdn`，配置为根 `conf.json`；本地 HTTP 使用 8087，游戏 MCP 仍为 3833。没有向外部服务器发布。

## UI 与技能边界

BattleWindow 管理独立 StatusPanel 和 ActionsPanel；状态组件及刷新缓存已从 HUD 移出，招募动画、构造布局也分文件维护。所有区域共用分层根 Canvas 的缩放和渲染相机，避免嵌套根 Canvas 缩坏按钮。窗口隐藏/关闭清除输入目标、招募动画与临时确认状态。

3D PlayAnimator、命中、弹道、特效、定帧与支持的运镜均走共享播放器；运行时每次克隆时间轴配置，避免并发单位相互改写。Animator 采样按所有权恢复，受击不抢占施法/死亡/受控动画。纯规则层决定伤害和中断，预览播放不结算伤害。历史 Clip 是否支持实战以技能 catalog/validate 实际返回为准，不能将所有类型一概写成已支持。

MCP 技能工作流为 catalog → read_timeline/revision → edit dryRun → edit → undo/redo → save/load → seek/snapshot/record → 正常战斗对照。战斗扩展升级 1.7.0，保留 schemaVersion 6 导航快照，get_view.presentation.framework 新增启动、包清单、引用、对象池和窗口的真实状态。

## 验证与范围

- `Reports/Framework/final-editor-smoke.json`：关闭本地 HTTP 后，默认 EditorSimulateMode 的启动、七包清单、玩家共用点击、招募/部署、Animator/UnitMono 与 Console 零问题通过；最终停在 Combat 编辑场景。
- `Reports/Framework/resource-build.json`、`final-artifacts.json`：七包构建成功，四个 DLL 同步文件与编译产物 SHA-256 一致。
- `Reports/Framework/hot-dlls.json`：Android 目标的四个热更 DLL 编译/同步；不是 APK。
- `Reports/Framework/skill-editor-mcp.json` 及 record-a/b：17 个技能工具、22 个标准技能配置/资源、双阵营录制与撤销重做已通过。
- `Reports/Combat/unity-verification.json`：使用 HostPlayMode + OfflineLaunch 构建包资源，正常招募、共用 pointer、拖拽/取消/松手、战术、12 波胜利、失败与重开、UI 边界及 Console 四类零问题；最终截图文件被占用后补存，续验说明已写入报告。
- `Reports/Art/unit-runtime-framework-20261125.json`：正常玩法期间只读采样九类角色、骨架姿态和 Shader；不是刷兵测试。
- `Reports/Combat/domain-tests-movement.json`：纯规则测试，包含三种子的完整远征；测试中旧 RVO 文案不代表当前启用 RVO。

构建包模式已实际请求本地 HTTP 清单并进入正常玩法；验证结束恢复 EditorSimulateDevice=false，默认 Play 不依赖 HTTP。启动配置和既有静态 UI/环境小资源仍内置 Resources，不等同于所有资源都已迁出 Resources。

用户本轮要求 Unity 验证，没有构建、安装或启动手机 APK。原生 HybridCLR 元数据注入、手机热更下载和设备性能没有据此验收，后续手机交付须先完成完整 GenerateAll/打包并运行项目移动测试流程。IAP 仍未接入。

## 2026-11-26 竖屏技能调校与验收

- 技能编辑器新增固定9:16监视窗，使用与实战共用的正交机位；Battle保持11.25半高，Detail按同一角度放大到4.6。近战/远程/巨像有不同测试间距，Scene视图同步对准舞台。capture、diagnostics、preview_stage通过已有command工具公开，资源查询默认限定Package_*，HQ缺资源直接回落Low，避免误读旧Resources空特效。新建模板改为3D PlayAnimator；录制返回投影类型、正交尺寸与比例。
- 修复40个生成特效只剩层级的问题，恢复源粒子、原Shader丢失后的纹理/颜色及URP材质；生成到Package_Effect并保留GUID，校验粒子和有效材质槽，主形状补充本项目刀光、雷击、冰晶、集火网格粒子。源工程只读。默认技能生成器只补缺失文件，不覆盖调校成果、不写旧技能镜像。
- 22份时间轴经过revision/dryRun/apply/save逐项修改；盾卫普攻接触点由0.20秒校到0.12秒并加轻定帧，调整目标侧法术、治疗受击动作及震屏/压暗，保持原伤害数值和玩法规则。
- 规则层从资源挂点、体型和施法距离计算远程到达时间，共享子弹沿用排程时长并追踪目标挂点。MCP战斗扩展1.8.0，状态仍为兼容schema6，skills新增timingMode/projectileSpeed，get_view的skillTimeline.active返回visualImpactAt供对照。
- `Reports/Skills/20261126/portrait-matrix.json`：22×双阵营×Low/HQ共88组配置/资源/竖屏取景/停止清理通过；HQ复用Low资源，不代表制作了独立HQ美术。`representative-records.json`：盾、弓、雷、冰四类双阵营JSON采样共8份，不是视频。
- `Reports/Skills/20261126/domain-tests.json`：118项纯规则检查、三种子12波远征通过。`Combat/unity-verification.json`：EditorSimulateMode真实招募、共用pointer取消/拖动/松手、12波胜利、失败/重开及Console四类零问题通过。
- `runtime-timing.json`：正常玩法观察1260个施法实例，10种普攻/基础技能时间轴的规则与表现命中排程最大差0.0287952秒，低于0.05秒规则步长。这是排程比较，不是逐像素接触或手机延迟测量。
- `branch-runtime.json`：从零正常招募升星，分别选择堡垒/穿透/强化雷池/深寒与战吼/飓风/链雷/冰甲两组分支，八种分支均已观察到正常命中。与基础技能共用时间轴的雷池/深寒只统计已选择分支的来源单位；第一组在第8波的奖励阶段恢复验证，未跳波、刷兵或改数值。两组排程最大差分别0.028693335与0.028480159秒，Console四类零问题。22份技能的编辑器配置、双阵营和逐项截图见review与portrait-matrix记录。
- 本轮没有构建手机APK、资源包或重新发布HybridCLR产物；默认EditorSimulateMode从当前源资产验证，旧已构建资源包不视为同步更新。手机、IAP和设备性能仍未验收。
