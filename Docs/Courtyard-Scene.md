# 遗迹庭院场景接入

日期 2026-11-24。用户授权尽量完整制作场景，并确认本次指定 AutoClash 资源可使用、不再核对商用授权。原工程保持只读。本轮不修改转盘概率、UI操作或战斗规则。

## 资源与渲染

- 从 `E:/Project/AutoClash_20210125/AutoClash1/Assets/Zhan/New Scene/Mesh` 和其中 Dungeon Crypt 模型目录提取20个模型，直接维护于 `Assets/LittleGame/Resources/Courtyard`。
- 从 `F:/Git/kabu_mobile/demo/client-demo-assets/Assets/Art/Shaders/Scene/Simple.shader` 复制到本项目 `Resources/CourtyardSceneShader.shader`。使用 URP `Scene/Simple`；显式Resources引用使Android构建包含Shader。
- AutoClash为Unity2019.4内置管线/Gamma，当前项目为Unity2022.3/URP14/Linear，因此没有照搬旧材质和旧灯光，也没有全场添加Gamma补偿。模型统一应用本项目石材、苔绿、金属嵌条和符文配色。
- 模型在编辑器按只含静态网格导入，不导入旧动画、相机、灯光、材质和玩法脚本；开启可读用于运行时静态合批。可用菜单 `Little Game/Combat/Prepare Courtyard Assets` 重新导入。
- 来源、目标和原文件SHA256记录于 `Reports/Combat/courtyard-source.json`。

## 当前场景

`CourtyardEnvironment` 独立负责地砖、岛台基础、外围墙、石柱、岩石、低密度植物、敌方拱门、晶石、阵位标记、城门门槛与城防。装饰布置在三路可交战区之外。使用低对比铺装减少角色与技能辨识干扰，远端拱门建立战场方向。

低石隔栏只读取 `BattleSession.FencesUp`，升起/下沉独立动画。首领准备阶段撤栏，恢复普通波时升起；不新增碰撞、不改变路径与射程规则。静态地面和外围装饰合批，动画隔栏不合批。

灯光采用暖色主光、三色环境光和主光阴影。没有为了场景效果接入旧工程后处理、Astar、旧UI或额外服务。

## MCP与验证

`little-game.combat` 扩展版本更新为1.0.1，`get_view` 的 `presentation.environment` 新增主题、模型实例数、缺失模型/材质数、Shader支持、隔栏段数及动画进度、环境碰撞数、静态合批Renderer数。所有旧操作保持参数不变。没有增加或改变玩法指令。

`Tools/Preparation/verify_combat.py` 已补充环境完整性、无玩法碰撞、合批和首领隔栏完全下沉检查，并继续通过玩家共用pointer和正常操作验证招募、拖拽、完整十二波胜利、失败与重开。

场景截图为实际GameView帧。Renderer数量和静态合批状态不等于实际DrawCall、GPU耗时或手机FPS；真机性能需另外采样。手机连接、最新构建与安装结果以本轮 Reports/Mobile 和 build-status.json 为准，不用编辑器检查替代真机检查。
