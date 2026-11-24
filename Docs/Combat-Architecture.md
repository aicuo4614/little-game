# 军团客户端架构与实现基线

日期：2026-11-23。当前用户已认可逐项讲解的战斗，授权按 Demo 重构整个客户端和战斗专用 MCP；不提交或推送。

## 规则来源

以聊天中后来讲解并认可的规则为准，覆盖 `Docs/Design/2026-11-23-combat-review.html` 的早期提案。初稿中的按列必出兵、单次招募、通用印记、传承槽等不进入本轮实现。

- 12 波远征，第 6、12 波首领。城门初始 10 点，普通漏怪 1 点；精英 2 点、首领 10 点沿用初稿的验收初值。
- 三行三列转盘，仅横线三同出一兵，可同时出多兵；每波 3 转，不结转。连续两转未出兵，下一转至少一条线，保底进度跨波保存。
- 第 4、8 波通过后三选一奖励。援军令只选一次，之后每波 4 转；其他奖励加强雷区或冻结。开局教学前两转有明确预设，不作为概率结论。
- 上阵九格、候补六格。出兵结果先暂存，容量不足时明确处理后继续，禁止静默丢兵。允许回收候补，为原文未定义的容量边界作必要补全，不引入付费或材料经济。
- 三个同职业同星级合成更高星，最高三星；宿主保留位置，消耗另外两名。升二星选择技能分支，三星保留宿主分支；明确预览后提交，分支不能随意切换。
- 普通波低矮栏隔开三路。近战只守本路；远程优先本路，射程内可支援相邻路，不能跨两路。集火也不能突破射程。
- 首领准备时撤栏，首领居中。近战允许跨路接敌，远程按射程。首领免疫击退，冻结改为等时长减速。首领重击有路线与蓄力预警。
- 每波 1 次指挥，在集火敌人和保护一路中二选一。瞄准暂停，不扣次数；确认后消耗，取消恢复。战中不允许布阵合成。
- 单位当波死亡、下波满血复原。清完敌人且城门存活进入波后阶段；45 秒仍在场敌人按漏怪处理。最终波存活获胜，城门 0 立即失败。可重开。

## 目录与依赖

| 位置 | 职责 | 允许依赖 |
| --- | --- | --- |
| `Assets/LittleGame/Client/Domain` | 可复现随机、招募、军团、关卡、战斗、指令、事件和快照 | .NET；不依赖 Unity、MCP、平台 |
| `Assets/LittleGame/Client/Application` | 单场宿主、统一指令入口、通知、存档接口 | Domain、Demo 迁入的 PureMVC |
| `Assets/LittleGame/Client/Presentation` | 三维战场、角色、分栏、投射物/技能反馈、UI | Application；只读取 Domain |
| `Assets/LittleGame/Client/Input` | 触屏/鼠标、点击、拖拽、取消 | Application、Presentation；提交正常指令 |
| `Assets/LittleGame/Client/Platform` | 本地存档与诊断；IAP 仅接口边界 | Application；无平台 SDK 进入 Domain |
| `Assets/LittleGame/Client/Bootstrap` | 创建场景依赖、组装、每帧推进和销毁 | 上述客户端模块 |
| `Assets/LittleGame/Client/Editor` | 场景安装、战斗 MCP、截图、验证 | 客户端、Demo MCP 扩展契约 |
| `Assets/LittleGame/ThirdParty` | 从 Demo 直接复制的通用能力和来源记录 | 按原许可和依赖维护 |
| `Packages/com.kabu.unity-mcp` | Demo MCP 编辑器、服务端源码，项目内直接维护 | 不通过子模块或 PackageCache 修改 |
| `Archive~/TeardownClient` | 旧玩法、旧 MCP、旧场景和工具归档 | Unity 不导入，不参与当前工具发现 |

先迁入可独立使用的 PureMVC 通知和 GameDebug，保留来源及版权头。新即时战斗参考 Demo 的 BattleContext、Runtime、View 和流程层分离方式；不搬入其依赖服务端协议的回合制结算和无关业务。技能编辑器完整依赖 Spine、DOTween、Rendering 与 UnitCore，先审计再接入，不能宣称接口文件存在就是本项目可运行。

## 启动和生命周期

`Combat` 默认场景 → `ClientBootstrap` → 平台服务与 `BattleApplication` → 注册指令路由和视图 → 默认远征直接可操作。新局/重开重建纯状态；帧更新只按固定步长推进战斗。退出 Play 或场景卸载注销 MVC、多余相机与静态引用。玩家与 MCP 同用 `Submit(BattleCommand)`。

快照、事件均由规则层生成；规则修改不直接调用相机、UI 或 MCP。冷却、伤害、胜负与随机必须能在无 Unity 条件下回归。持久化带版本，恢复后校验，写入失败不覆盖旧档。

## 验收

先纯规则回归，再编译与 Play，通过正常 MCP 实际招募、部署、合成、战术、奖励、失败/胜利、重开及状态观察；另走共用 pointer 入口检查取消、拖动和按钮。检查 Console 和真实渲染。Android 设备可用时重新构建安装启动，记录前台进程、画面、触屏与日志；不可用时明确阻塞，不装旧包。
