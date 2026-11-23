# 当前工作：结构样板（2026-11-21）

新增独立 Structure 场景：右上旋钮退出并停靠，左上销轴约束盖板受重力矩和阻尼下摆；通路打开且运动减慢后，单击绿色模块握持拉柄沿导轨抽出。拖动只环绕相机，装置与重力固定；目标取出即完成，保留盖板和支架。可重开，界面可往返原版三关。结构说明见 Docs/Structure-Sample.md。

Runtime/Structure 分离结构定义、纯规则/运动、表现、输入、界面与启动；网格细节、实体包络、连接坐标和退出通路共用定义。MCP little-game.structure 1.0.0 提供状态、结构检查、视图、共用 pointer/正常操作及截图；旧 little-game.puzzle 3.3.2 增加 open_structure，并返回样板入口按钮。

最终编辑器回归 208 条请求、8 组关键检查通过，851 个摆角采样无非预期实体交叉，实际网格不超出结构包络，Console 四类问题为 0。截图 Reports/structure-*.png。模型是受约束摆动样板，不是自由刚体仿真；两次有效操作尚未证明谜题深度。无已连接 Android 手机，未安装或真机验证；检测记录 Reports/Mobile/structure-device-check.json。真实触屏脚本 Tools/Mobile/verify_structure_touch.py 已准备但本轮未在手机执行。

Android 测试包配置更新为 1.4.0 / 5、IL2CPP ARM64，首场景 Structure，次场景 Game。最新成功构建以 Library/MobileTesting/build-status.json 为准；最终交付副本与哈希以 Reports/structure-delivery.json 为准。不要把以下历史包当作结构样板。本轮未提交或推送代码。

## 历史交付：原版 1.2.0

当前游戏《电器回收站》1.2.0，Android版本号3。用户自行安装验证，不要要求连接手机或代为安装。

已交付：第一关旧掌机8件，第二关电脑机箱15件。电脑外观重做成厚实方正机箱、前置双风扇、接口、电源键和脚垫。第二关多分支拆卸依赖，顶部“第二关”可直接选关，第一关通关后也能正常进入。

回收位改为相机平面，外框、实际网格轮廓和落位共享中心及朝向，八列排列，第二关两排；不重复计入合并前已禁用的网格。动画先外法线脱离，再进入设备前方安全平面，绕行缩放旋转并落格，飞行中锁定旋转、翻面、切关；重开可取消。回收件取消投影阴影。

机壳/机箱框架作为最后一件有独立格子，需先回收所有其他件。最后飞行时保持7/8或14/15，格子不绿、phase非won；落位后才8/8或15/15、全部变绿、工作台清空并成功。

MCP 3.1已同步关卡数据、load_level、next_level、recoveredCount、detached/collected、motion安全间距、visualCenter及回收格行列中心；玩家与MCP共用规则，pointer验证真实输入。

最终编辑器验证：双击285条、规则50次、两关1122条请求通过，含混合角度、第二关全部件pointer双击、主板后再回收框架、对齐与安全间距、最后落位完成。Console四类问题为0，已退出Play。详见Docs/Prototype.md、Reports/levels-verification-summary.json和对应回放。最终画面Reports/workbench-computer-front/open/last-flight/complete.png已检查。

APK：Builds/Android/electronics-recycling-1.2.0.apk，29,426,435字节。构建id 228285da4db14994a8daf6b95084e778。APK ZIP、版本、标签、包名和IL2CPP ARM64校验通过，摘要与SHA256在Reports/workbench-delivery.json。未手机实测，等待用户试玩反馈。未提交或推送代码。冰箱等后续设备尚未制作，需先保证外观辨识度。
