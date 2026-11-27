# 战斗 HUD 改版验收 2026-11-27

## 实现范围

- 顶部使用“遗迹庭院”关卡名、十波圆点进度和人口图标。完成当前波后填满节点并推进连线，第 5、10 波使用首领图标。移除文字波次、存活军团和推进距离展示。
- 左侧羁绊小方块按行排列，每行四项，自动换行。仅显示上阵人数大于零的种族和职业，按不同英雄去重，人数显示下一档门槛，圆点显示激活档位。方块和波次圆点根据实际 UI 根区域宽高保持比例。
- 右上侧统一伤害面板支持本波／本局，按英雄汇总并排序，显示实际 UnitPackage 模型快照头像、星级、伤害条和数值，每 0.5 秒刷新，波后立即刷新。面板按行数伸缩，最多显示八种英雄。
- 实际扣血记账，护盾吸收和溢出伤害不计入。普通攻击、主动技能和持续伤害共用结算。阵亡和撤下保留贡献，下一波开战清本波，整局累计和最近一波结果保存到检查点，重开清空。旧存档缺少统计字段时从空统计开始。
- 取消战术集火、区域保护、瞄准暂停和准星。历史指令枚举保留数值兼容，执行时明确拒绝。技能护盾仍按各自技能结算。
- 共用输入可以切换伤害统计，候补卡片也使用英雄专属头像。战斗底栏收为暂停和倍速。

## 素材和 MCP

- 复用 PELibDemo Fantasy RPG 的面板、卡片、按钮和图标。26 项来源、哈希与九宫格边距见 Reports/Art/ui-source-migration.json。头像从 Assets/Package_Unit/Portrait/Base 加载，增加 Portrait 类型地址规则，EditorSimulateMode 已验证。
- 战斗扩展 2.1.0，状态 schemaVersion 8。get_state 增加 damageStatistics，get_view 增加 panels 中的可见羁绊、伤害榜值、统计范围和进度。扩展目录和实际 tools/list 均移除旧集火与保护操作。
- MCP Server 源码构建成功，0 错误。保留两条现有警告，分别为 SkillEditorSeekTool 的可空参数和 MCP Stateful HTTP 兼容 API。

## 实际验证

- 纯规则 115 项检查及三个种子十波流程通过，包括扣血统计与事件一致、保存恢复、新波重置、整局保留、重开清空、旧指令拒绝。输出 domain-console.txt，规则报告 Reports/Combat/domain-tests.json。
- A*／目标稳定性／死亡重选／快速转身与 FSM 回归通过。
- Unity 2022.3.62f3c1、WindowsEditor、540×960 正常招募部署，完整十波获胜。每波伤害均与实际命中事件相等，并验证整局增量。逐波通过玩家共用 pointer 点击本波／本局，检查布局边界、按钮无交叠和触屏高度。结果见 unity-validation.json。
- 修复首轮截图发现的浅色羁绊底图、数字／星级裁切和空榜标题裁切。调整后完成上述十波验证。最终再完成取消拖放、双击部署、暂停继续、两种统计切换和画面检查，见 final-input-check.json。
- 最终 Console Error、Exception、Assert 均为零。退出 Play。截图 fighting-final.png、preparation-final.png 为最后一轮实际画面，wave-result-10.png 为十波通关画面。
- ADB devices -l 未发现连接设备，本次未构建或安装 APK，手机布局、触屏与热更资源包表现尚未验证。编辑器截图和规则测试不作为真机验收。

## 顶部对齐修正

- 用户反馈新增两块面板距顶部过远，已将羁绊首行和伤害统计的顶边统一锚定到顶部面板下沿，三者共用 HeaderBottom，消除原先空隙。
- 准备阶段操作提示移到布阵区下方、招募面板上方，避免占用顶部对齐区域。
- MCP get_view.panels.layout 返回 headerBottom、damageTop、synergyTop，本轮实测均为 0.85。验证流程已加入对齐断言。
- Unity 正常招募、部署、开战、暂停继续、本波／本局点击及波后完成通过，Console Error 为零，已退出 Play。实际截图为 alignment-preparation.png 与 alignment-fighting.png，结果见 alignment-validation.json。本轮未做真机检查。
