# 固定机位与无雾战场

验收环境为 Unity 2022.3.62f3c1 Windows 编辑器，Combat 场景，540 × 960 的 9:16 GameView。纯规则回归覆盖种子 173、7、91。编辑器正常远征使用种子 173。

| 项目 | 结果 |
| --- | --- |
| 脚本编译与资源导入 | Unity 编译通过，最终运行时庭院和角色 Shader 可用 |
| 布阵与共用输入 | 空地横滑时朝向、位置和选中状态稳定。左边缘部署、取消、重新命中及召回通过 |
| 普通波入场与瞄准 | 敌军从右侧进入，入场血条和左侧友军完整可见。共用输入完成集火点选，瞄准期间停止平移，波后回到布阵位置 |
| 首领预警 | 第 6、12 波实战截图中首领与整条横向预警均在画内 |
| 正常玩法 | 纯规则三种子 12 波获胜。Unity 从零招募完成 12 波胜利、单兵团灭和重开 |
| 角色材质 | 正常战斗采样观察到 9 类单位，使用的 Roco/Character Shader 均受支持 |
| Console 与收尾 | 最终完整回归的 Error、Exception、Assert 和 Warning 均为 0，退出 Play 后为 Stopped |
| Android | 本轮仅验证 Unity，手机包未构建 |

首版采用正面正交机位。战中最多前移 1.4 米，并为最左侧友军预留空间。首领存活期间进一步限制前移范围，以容纳横向预警。较窄屏幕保留基础横向视野，实际画面验收使用 9:16。撤掉两侧厚雾与角色、场景的边缘染灰，右侧路肩以残门定位敌军入场。

[普通波入场](combat-enemy-entrance.png) · [左缘布阵](combat-deploy-left-edge.png) · [第 6 波预警](combat-boss-warning-6.png) · [第 12 波预警](combat-boss-warning-12.png) · [完整回归](unity-verification.json) · [单位材质采样](unit-renderers.json)

另以正常招募走到第 6 波，暂停前后的动作和动作时间一致，恢复与 Console 检查通过。[暂停复验](unit-pause-verification.json)
