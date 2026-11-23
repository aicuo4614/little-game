# 结构样板：悬挂盖板与导轨模块

## 范围与架构

独立 Structure 场景，沿用 Unity 2022.3.62f3c1、URP 与项目字体。模型在 Unity 内制作，无新增付费资源或依赖。旧三关保留于 Game 场景。

Runtime/Structure 下：StructureDefinition 是结构尺寸、装配坐标、连接方式和几何判断的唯一来源；StructureSession 是不依赖 Unity 的正常玩法和固定步长约束运动；StructureView 读取状态建立几何并表现；StructureInput 是触屏、鼠标和 MCP 共用输入；StructureHud 只显示状态与调用指令；StructureBootstrap 负责启动。

左上销轴沿 Z 轴，右上旋钮锁定盖板。旋钮先旋转退出，再移至有实体支架的停靠位，完全退出后才释放盖板。销轴、锁孔、停靠座、背板和导轨各有可见支撑。重力矩与阻尼驱动单自由度摆，固定步长 1/120 秒，角度限制 -85 至 0 度；重心偏置决定约 -56 度的平衡姿态。该机制是受约束摆动，不是自由刚体或机械精密仿真。单次运行推进最多补偿 .25 秒，严重卡顿时运动时间会变慢。

模块沿世界 -Z 导轨移动，盖板用 OBB，模块完整退出的连续扫掠体用 AABB，以分离轴检查通路。另要求盖板角速度小于约 8 度/秒才允许抽取。点击抽取代表玩家握住拉柄，moduleHeld 表示抽取后仍被玩家持有，不将悬空静止解释为无重力。目标完全退出后胜利，支架和盖板保留，不要求拆空。支持运动中重开。

观察相机绕固定装置环绕，装置与重力不随观察变化。单击旋钮解锁、单击模块尝试抽取；起拖只改变相机，取消、多指及失焦清除当前手势。查看结构显示盖板边界、销轴与导轨出口辅助线。

## 验证契约

MCP little-game.structure 1.0.0：get_state / get_view / get_structure / pointer / interact / restart / capture_view。正常指令与玩家相同；pointer 用于输入验收。get_structure 提供全摆角范围每 .1 度采样的静态实体交叉检查，预期销轴接触除外；采样不是连续碰撞证明，仍需截图及运动检查。

首轮验证关注：锁定时抽取拒绝、旋钮退出前盖板固定、下摆与阻尼、模块通路、观察不转动装置、点击/起拖/取消/松手、旋钮运动及摆动/抽取中重开、胜利与重复运行。手机 APK 不等于抖音运行环境验证。实际结果由 Reports/structure-verification*.json 和截图记录，不能把设计目标当作通过结果。

## 首轮结果（2026-11-21）

Tools/Preparation/verify_structure.py 最终通过 208 条请求、8 组关键检查（轮询请求数随运行时序变化）。全摆角范围 851 个采样未发现非预期实体交叉，盖板最低点高于底座 .105 米；各部件实际网格细节未超出声明的实体包络。采样结果不代表任意未知模型都能无穿插。正常玩家 pointer 全流程、取消、起拖、远距离松手、运动中重开、目标完整取出及旧三关往返通过，Console Error / Exception / Assert / Warning 均为 0。

截图 structure-locked / unlocking / swinging / ready / structure-guides / extracting / complete 已生成并检查。这个关卡目前用于验证结构可信度、反馈和实现流程，仅有两次有效操作，尚不足以证明谜题深度或商业表现。后续应基于试玩决定是否加入第二种操作顺序。

Android 真机检测没有发现已连接设备，记录 Reports/Mobile/structure-device-check.json；未执行安装或声称手机通过。Tools/Mobile/verify_structure_touch.py 已准备，用 ADB 真实触屏和开发包每 .5 秒只读观测验证，不注入玩法状态。该脚本本轮仅完成语法检查，未真机执行。

最终 Android 1.4.0 / 5 已成功构建，IL2CPP ARM64，29,635,633 字节。交付副本 Builds/Android/structure-sample-1.4.0.apk，构建 ID bbefb40a3ea34d549fe952291906b98c。APK ZIP CRC、包名、版本与 ARM64 ELF 架构检查通过，SHA256 及验证摘要见 Reports/structure-delivery.json。构建后编译成功、Console 四类问题仍为 0；编辑器停在已保存的 Structure 场景，退出 Play。未提交或推送代码。

完整字体源缓存于 Library/FontSource，来源 google/fonts 的 OFL Noto Sans SC。Tools/Preparation/update_game_font.py 可重新生成全部运行时字符子集。Demo 最新指定路径 F:/Git/kabu_mobile/demo 已确认存在，client-demo 的 Unity 版本与本项目均为 2022.3.62f3c1；本轮按项目已有的规则/输入/表现/MCP 分层增量接入，没有搬入其他业务框架。
