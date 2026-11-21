# 《电器回收站》当前原型

## 产品与本轮范围

题材从《拆机救主板》调整为电器拆解回收。当前只有旧掌机这一件设备，用于验证“观察结构、按依赖双击拆卸、零件飞入匹配回收位”的核心流程。电脑、电视、洗衣机等是后续候选，尚未制作；没有接入经营、平台广告或证明收益。

Unity 2022.3.62f3c1，URP 14.0.12 + Linear，竖屏9:16。源码与渲染依赖在本工程Packages维护，配置在Assets/LittleGame/Rendering；Android开发包使用IL2CPP ARM64。

## 当前操作与规则

- 拖动机器任意位置（包括螺丝、接头、后盖等）自由旋转，松手保留角度。空白处拖动不旋转，点空白取消选择。翻面按钮回到标准正面/背面。
- 单击部件只选中，绿色发光强度来回变化，不直接拆卸。
- 同一部件两次点击松手间隔不超过0.36秒，且距离不超过屏幕短边8%，执行双击拆卸。移动达到短边1.2%（最少6像素）则进入旋转并打断双击。取消、失焦、多指、UI点击或中间有其他规则操作都会使双击失效；长按超过0.6秒不算轻点。
- 所有部件使用同一条disassemble指令。无需选择法线或拖出方向，没有取出箭头和取出按钮。角落常驻“拖动旋转 · 双击拆卸”。
- 正常流程：两颗外壳螺丝 → 后盖 → 排线接头 → 电池 → 主板固定螺丝 → 主板。两颗外壳螺丝可互换先后，主板螺丝露出后也可提前取下；不能绕过遮挡、连接和固定件。
- 双击失败时，被操作部件红闪、轻微抖动约0.65秒；阻挡件高亮为橙色，并显示具体原因。连续失败刷新反馈，不累积位移。失败不增加回收数。
- 拆卸动画约0.55–0.8秒，期间不能拆下一件。螺丝先旋出，其余部件先抬起，再飞入自己的收纳位。重开可以中止动画并恢复完整装配。
- 顶部七个回收位按上述顺序从左到右排列，依据部件及子网格实际包围盒制作对应比例的圆形/圆角格子与浅色模型轮廓。每件按展示比例缩放，归位动画结束后格子变绿。收集位置固定，不跟随机器旋转。
- 收齐七件后完成整机回收，显示7/7和再来一次。当前仍为简化拆解模型，不是现实电器维修教程。

## 模块与依赖

- Rules/PuzzleSession：纯C#规则、朝向、依赖、回收状态、动画时间、选择及失败事件；不依赖Unity UI或MCP。
- Presentation/PuzzleInput：鼠标/触屏的拾取、点击时间及位移判定、旋转；玩家和MCP pointer使用相同入口。
- Presentation/MachineModel、DeviceMesh：机身与可拆件的程序化模型、独立部件材质和碰撞体。
- Presentation/MachineView、PartMotion：读取规则，更新旋转、发光和抖动，将拆下部件从旋转层级转到固定回收位。旧的拖动预览与方向箭头已移除。
- Presentation/CollectionBar：从模型计算回收位大小、位置、轮廓和展示缩放，落位后更新绿色状态，不实现拆卸规则。
- Presentation/WorkbenchView：固定木桌、防静电垫、工具与灯具，环境按材质合并；不承担回收状态。
- Presentation/RepairHud：任务卡、七件进度、安全区、反馈、翻面/重开/完成按钮。
- Bootstrap/GameBootstrap：打开Assets/Scenes/Game.unity后直接Play，自动生成完整设备、环境和界面。
- Editor/PuzzleMcp：MCP参数和返回适配，不实现第二套规则。

中文使用Noto Sans SC Regular子集，修改文案需重新生成Assets/LittleGame/Resources/GameFont.ttf；许可位于Resources/OFL.txt。模型、场景与轮廓由工程现有网格生成，本轮没有新增外部美术资源。

## MCP 3.0

端点http://127.0.0.1:3833/mcp，操作前确认dataPath为E:/Project/little-game/Assets。通过unity_extension_catalog查询little-game.puzzle扩展。写操作传allowWrite=true。

| 操作 | 行为 |
| --- | --- |
| get_state | phase、stage、revision、moves、animating/animationPart/animationProgress、orientation、backFacing/backVisible、selected、连接状态、accepted/message/blocker、failureSequence/failedPart、lastAction/lastTarget和availableActions |
| get_view | 部件局部/世界位置、世界缩放、实际emission、屏幕坐标、parked、accessible；gesture、interactionVersion、doubleClickSeconds；collection各格part/index/尺寸/显示比例/filled/位置；feedback失败状态与选择呼吸；hud提示及按钮坐标；workspaceBounds旋转包围球与回收栏间距 |
| pointer | begin/move/end/cancel，x/y为左下原点0–1视口坐标，执行玩家共用点击、双击和拖动入口 |
| disassemble | part为screw_a、screw_b、cover、cable、battery、board_screw、board，对应双击的正常拆卸命令，使用同一依赖与动画校验；不模拟点击时序，时序验证用pointer |
| select / clear_selection | 正常选择或取消选择 |
| rotate | delta、tilt、roll，绕屏幕上/右/前轴的角度增量，四元数累计，无俯仰限幅 |
| orbit | fromX/fromY/toX/toY为相对旋转中心、按轨迹球半径归一化的坐标，和玩家拖动共用算法，灵敏度1.8 |
| flip / restart | 标准翻面 / 完整恢复设备、回收格和计数 |
| performance | 最近240帧的实际间隔及平台/分辨率；编辑器数据不能宣称手机帧率 |

旧tap、drag操作和direction参数已删除，get_view不再返回pullVector、extractionNormal、previewPart或previewLift。正常工具不提供跳关或解除依赖的调试能力。

## 验证和交付

2026-11-21最新源码编译成功。Tools/Preparation/verify_double_click.py记录190条输入/状态回放请求，覆盖单击不拆、慢速点击不拆、换部件不拼双击、在螺丝/后盖上拖动只旋转、取消打断双击、背景忽略、实际选择发光变化、失败红闪/抖动/回位、动画期间拒绝连续拆卸、七格顺序归位与变绿、固定世界位置、全部回收和重开。报告Reports/double-click-verification.json。

Tools/Preparation/verify_input.py为当前双击回归入口；verify_gameplay.py通过49次正常规则操作，包含多轴整圈旋转、单位四元数、背面与遮挡、连接依赖、动画锁、完成和重开，并验证旧操作从MCP目录移除。报告Reports/gameplay-verification.json。两项回归后Console错误、异常、断言和警告均为0。

capture_workbench.py采集Reports/workbench-front.png、back、selected、blocked、flight、open及complete七张竖屏画面，已检查匹配回收位、反馈颜色、中文、主体与回收栏间隔。截图属于编辑器相机渲染，不能替代手机验证。

用户明确自行安装与验证。本轮仅执行python -X utf8 Tools/Mobile/mobile_test.py --build-only，不检测或安装手机。构建结果在Library/MobileTesting/build-status.json，成功包默认Builds/Android/little-game.apk，版本1.1.0（Android版本号2），包标识com.littlegame.teardown.dev。最终包和校验记录以Reports/workbench-delivery.json为准，不复用旧包冒充新版本。

手机自动验证工具仍保留：mobile_test.py默认重新构建、更新安装并启动，--install-built只用于明确复用成功构建。verify_touch.py已同步双击和回收栏契约，尚未在本轮新包真机执行。Android Development包的MobileDiagnostics每0.5秒只读输出observation.json，包含状态、视图和性能，不修改玩法；性能测量需注明该落盘开销。手机APK不是抖音运行环境，抖音适配仍待单独验证。


最终构建已成功：Builds/Android/electronics-recycling-1.1.0.apk（同时保留默认little-game.apk），29,388,785字节。已校验APK ZIP完整性、IL2CPP ARM64库、com.littlegame.teardown.dev包名、版本1.1.0/2及“电器回收站”应用标签，交付副本与默认产物SHA256相同。构建id c2329977be474b54a15e44685def2787。构建后再次确认编译成功、已退出Play、Console错误/异常/断言/警告均为0。未向手机安装，也未宣称本轮真机验证通过。
