# 《电器回收站》1.2.0 当前原型

## 范围与操作

现有两关：第一关旧掌机，含机壳8件；第二关电脑机箱，含机箱框架15件。当前验证拆解、回收与关卡推进，没有接入经营、平台广告或证明收益。冰箱等设备尚未制作，后续应先验证整机辨识度，不能只替换内部零件。

- 拖动设备任意位置旋转，单击选中呼吸，0.36秒内对同一部件双击尝试拆卸；拖动、取消、失焦、多指、UI点击或中间发生其他规则操作都会打断双击。背景拖动无效，点空白取消选择。
- 保留遮挡、连接和固定件依赖。失败时被操作部件红闪、抖动，阻挡件橙色高亮并说明原因。失败不计回收进度。
- 回收动画约1.25秒（螺丝）或1.45秒（其他部件）：沿装配法线向外脱离，进入设备前方安全平面，再向外弧线转移，最后落格。仅在安全平面转移阶段缩放、转成收纳朝向。动画中锁定旋转、翻面及切关，重开可以中止并恢复完整设备。
- 每个回收位的外框、单色轮廓和最终零件共用几何中心与朝向。基于启用的可见网格计算包围盒，避免重复计入合并前网格。回收栏采用相机平面、一关八列，第二关八列两排；格子大小保持零件比例，行列整齐，归位后变绿。
- 机壳/机箱框架也占独立回收位，必须先收完内部件。最后机壳飞行时仍未通关，格子还未变绿，进度停留7/8或14/15；落位后才计满、清空工作区并成功。
- 第一关完成后可点击“下一关 · 电脑机箱”；也可在顶部点击“第二关”直接试玩。顶部可切回第一关，重开保留当前关卡。

## 关卡与模型

第一关：两颗外壳螺丝 → 后盖 → 电池排线接头 → 电池 → 主板固定螺丝 → 主板 → 机壳。同阶段允许合理交换顺序。

第二关：三颗机箱螺丝、侧板、主电源接头、电源模块、显卡固定螺丝、显卡、内存卡扣、内存、散热器固定螺丝、处理器散热器、主板固定螺丝、主板、机箱框架。打开侧板后形成多条支路：电源/显卡/散热器受电源连接影响，显卡另有固定螺丝，内存先解卡扣，散热器另有固定螺丝；主板需先移除其上模块；最后回收框架。

电脑外壳独立制作成有厚度的方正机箱，包含前置双进风风扇、I/O接口、电源键、脚垫、侧板及通风槽。开局提供能看见前面板的倾斜观察角度，侧板朝上后可拆。不是掌机外形放大版。模型为简化低多边形风格，当前并非现实维修教程。

## 技术与模块

Unity 2022.3.62f3c1，URP 14.0.12 + Linear，竖屏。Android Development APK使用IL2CPP ARM64，包名com.littlegame.teardown.dev，版本1.2.0（Android版本号3）。

- Rules/DeviceLevels：纯C#设备、部件名称、遮挡和依赖数据。
- Rules/PuzzleSession：当前关卡、朝向、拆下/已回收的区别、动画时间、选择、失败事件、完成与切关；不依赖Unity UI、广告或MCP。
- Presentation/PuzzleInput：玩家和MCP pointer共用的命中、点击时序、取消及旋转入口。
- MachineModel/DeviceMesh：各设备几何体、静态机壳分组与碰撞体；程序化模型与材料在本工程维护。
- MachineView/PartMotion：读取规则并更新姿态、反馈与安全回收路径。切关重建该设备独立的表现根节点，销毁原设备及已飞出部件。
- CollectionBar：从实际启用网格生成居中轮廓，计算回收位尺寸、行列、朝向与落点，落位后变绿。
- WorkbenchView/RepairHud：固定桌面、垫子、工具，以及安全区内的关卡/进度/反馈/操作界面。
- Bootstrap/GameBootstrap：Game场景直接Play即可进入第一关。

使用Noto Sans SC Regular子集，新增部件和界面文案后同步更新GameFont.ttf；许可位于Resources/OFL.txt。本轮无外部美术素材、付费资源或SDK接入。

## MCP 3.1

端点http://127.0.0.1:3833/mcp；先核对dataPath为E:/Project/little-game/Assets。通过unity_extension_catalog查询little-game.puzzle，写操作传allowWrite=true。

| 操作 | 契约 |
| --- | --- |
| get_state | level/levelCount/device/totalParts/recoveredCount，parts的detached（已拆下）和collected（已落位），phase/stage/moves，动画及进度，视角、选中、失败原因与可执行动作 |
| get_view | 部件实际位置、发光、visualCenter、detached/parked；motion.stage及frontClearance/outwardClearance；collection的row/column、中心、尺寸、落点和filled；HUD按钮、手势及布局间隙 |
| pointer | begin/move/end/cancel，左下原点0–1视口坐标，走玩家同一输入入口 |
| disassemble | 当前关卡的部件ID，同一正常规则命令；点击时序仍须用pointer验证 |
| select / clear_selection | 正常选择/取消 |
| rotate / orbit / flip | 四元数自由旋转、虚拟轨迹球、标准翻面；飞行期间拒绝，避免设备进入既定路径 |
| load_level | level为1或2，与界面关卡选择一致，动画中拒绝；不是调试跳过拆卸 |
| next_level | 当前关最后一件落位完成后进入下一关，末关拒绝 |
| restart | 恢复当前关全部部件、回收格、进度和默认观察角度 |
| performance | 最近240帧间隔、分辨率和采样平台，编辑器结果不能宣称为手机帧率 |

旧tap/drag/direction及拖出箭头、预览参数仍已移除。parts列表与part参数枚举从关卡数据同步生成，避免第二关工具漏件。moves是接受的拆下动作数，界面进度与胜负使用recoveredCount；最后一件飞行中phase仍为animating。

## 验证与交付约定

Tools/Preparation/verify_double_click.py验证第一关玩家输入、双击边界、选中呼吸、失败反馈、回收和重开。verify_gameplay.py验证旋转、依赖、动画锁和完成。verify_levels.py覆盖两关、第二关全部部件的玩家双击、正常进入下一关/选择/重开、多支路依赖、安全路径采样、回收位中心对齐、最后机壳飞行未完成/落格才成功。实际请求记录分别保存于Reports/double-click-verification.json、gameplay-verification.json、levels-verification.json。

路径检查：clear_body阶段核对沿装配法线完全脱离；transfer阶段核对部件包围半径在整机包围球前方留有余量；settle阶段回收位位于整机旋转投影范围之外。仅在预定安全阶段旋转和缩放零件。画面仍需结合截图检查，不能把数学间距代替视觉验收。

capture_workbench.py采集两关外观、开盖、失败、回收和完成画面。Reports/workbench-computer-front.png用于主机外观复核，computer-open用于内部布局，computer-complete用于最终机箱框架归位和空工作台。截图属于编辑器相机渲染。

用户明确自行安装验证。本轮只执行python -X utf8 Tools/Mobile/mobile_test.py --build-only；不检测或安装手机。构建状态Library/MobileTesting/build-status.json，默认成功包Builds/Android/little-game.apk，明确版本交付副本与校验记录以Reports/workbench-delivery.json为准。

手机工具verify_touch.py已同步关卡数据、双击、已落位状态，可用--level 1/2选择验证关卡；本轮未在真机执行。开发包MobileDiagnostics每0.5秒只读落盘观测，手机性能采样需注明该开销。APK不等于抖音运行环境验证。


2026-11-21最终编辑器验证：双击回归285条请求，正常规则50次操作，两关专项1122条请求通过。两关专项包含第一关斜向混合旋转后的全流程、第二关15件实际pointer双击、最后机壳落位完成、框架回收、回收中心对齐和动画安全间距采样；统计摘要Reports/levels-verification-summary.json。最终截图已复核主机外观、内部布局、回收栏、最后飞行14/15及空工作台15/15，回收后的零件不再向桌面投射浮空阴影。
