# Little Game 技能编辑器

来自 `F:/Git/kabu_mobile/demo/client-demo-assets` 的窗口、Clip、预览与 MCP Host，已按本工程 3D Animator、UGUI 和共享 TimelinePlayback 适配。原始来源与改动记录见 `Docs/Framework-Migration.md`。

- 菜单 `Kabu/技能编辑器`，场景 `Assets/Scripts/Editor/TimelineEditor/Scene/TimelineEditor.unity`。同时打开固定 9:16 的 `Kabu/技能竖屏预览`，不依赖 GameView 的自由比例。
- 唯一 MCP 地址为 `http://127.0.0.1:3833/mcp`；技能工具 `unity_skill_editor_*`，正常战斗工具 `unity_combat_*`。
- 技能保存在 `Assets/Package_Unit/Timeline/Base`，绑定表在 `Assets/Package_Unit/Skill/Base/UnitSkillBindings.json`。实战从 YooAsset 加载同一份标准 TimelineConfig，不再维护 Resources 镜像。
- PlayAnimator 原生用于预览与实战。Spine 类型仅保留旧文档兼容，不作为当前 3D 单位生产方案。可用 Clip、字段、限制与默认值以 `catalog` 实际结果为准；部分历史片段仅支持预览。
- AI 操作：catalog → read_timeline(revision) → edit(dryRun) → edit → validate → save → load → seek/snapshot/record → 正常战斗对照。批量编辑支持 alias、undo/redo；保存检查外部文件变更并使用原子替换。
- validate 的配置/资源兼容结果不代表实战或手机通过；record 是编辑器预览世界 TRS 采样。伤害由规则层产生，预览播放不结算真实战斗。
- 验证脚本 `python -X utf8 Tools/Preparation/verify_skill_editor_mcp.py`。返回 Combat 后需重新 Play 才启动战斗。资源包默认走 EditorSimulateMode，不要求本地 HTTP。

## 竖屏调效（2026-11-26）

- 实战比例共用 `PortraitBattleCameraSettings` 的正交机位、角度及 11.25 半高；Detail 使用同角度的 4.6 半高放大，仅供细节检查，不作为实战尺寸结论。近战默认间距1.3米，远程3.6米，巨像2.4米；敌方预览使用 Grunt，双方朝向按阵营切换。
- `unity_skill_editor_command` 增加 `preview_stage(mode=Battle|Detail,distance?)`、`capture(width?)`、`diagnostics()`。切布局停止并复位，不写技能。capture 在暂停/seek完成后生成9:16 PNG，返回路径和当帧诊断，不含Overlay UI；diagnostics 返回投影、挂点、粒子、有效状态及材质。只显示拖尾的粒子允许不设主材质。
- `assets` 默认只查询 `Package_*` 实战资产；找未接入候选使用 `scope=all`。`snapshot`/`record`仍是JSON采样，camera.origin增加正交尺寸和比例，不是视频录制。
- 特效生成菜单 `Little Game/Combat/Build Skill Effects` 输出到 `Package_Effect/Low/Base`，保留GUID；源Shader缺失时从序列化属性恢复贴图/颜色，使用URP材质，检查粒子数量，并重新导入材质引用。重建默认技能只补缺失文件，不覆盖已调校JSON、不写Resources镜像。
- 逐技能编辑脚本 `tune_skill_effects.py` 使用revision、dryRun、apply及save，变更前后留在 `Reports/Skills/20261126/tuning.json`；`capture_skill_review.py` 仅采集评审图；`verify_portrait_skills.py` 检查22技能×双阵营×两档画质及清理。
- 原始 Hovl 资源继续作为辅助表现。近战刀光、雷击、冰晶和集火主形状由本项目网格粒子补充，直接由共享播放器采样，不依赖预览专用脚本。
- 远程实战按施法瞬间的距离、双方资源挂点与体型导出纯规则命中时间；子弹使用共享排程已确定的飞行时长，避免离弦时再次测距使命中片段与到达时间分离。移动目标和渲染插值仍按固定步长误差实测，不称为像素级同步。
