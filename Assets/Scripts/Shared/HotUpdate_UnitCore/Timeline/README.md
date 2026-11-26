# 技能时间轴播放

预览和实战的统一入口是 `TimelinePlayback`。宿主创建 `TimelinePlaybackContext`，提供角色绑定、候选单位、特效资源和相机震屏节点；时间推进和表现执行留在 shared。

```text
美术工程 TimelinePreviewPlayback ─┐
                                 ├─ TimelinePlayback → TimelinePlayer / TimelineSchedule
程序工程 BattleSkillTimeline ─────┘       ├─ TimelineClipPlayback（Spine / 受击）
                                         ├─ TimelineUnitPlayback（单位变换）
                                         ├─ TimelineEffectPlayback（粒子 / Animator / 特效 Spine / 点光源）
                                         └─ TimelineBulletPlayback（弹道）
```

- 实战调用 `Player.Play()`、`Tick(deltaTime)`；编辑器调用 `Player.Evaluate(absoluteTime)`。预览和 Seek 不触发命中回调；向后采样会释放旧实例、恢复单位并重建画面。
- `Stop()` 归还资源、恢复单位及相机；暂停保留画面。调用方在取消、切换技能、关闭窗口时负责 Stop。
- 支持 PlaySpine、PlayHit、PlayEffect、PlayBullet、Translate、Rotate、Scale、RoleRotateToTarget、CameraShake、HitStop、SetCastTargets、SetHitTargets。
- 历史编辑器专用片段通过 `SampleEditorClip` / `ExitEditorClip` 适配，仍由同一调度器决定片段边界；实战不提供此适配。`Supports` 和 `Validate` 是 MCP 能力目录、兼容报告和实战接入的共同依据。
- `TimelineClipXXX` 编辑器类保留参数绘制、资源选择和手柄。共有类型不再从 GUI 重绘执行原来的进出状态机。
- 技能 JSON 存资源逻辑名。编辑器从 AssetDatabase 解析；实战将其转换为 `Effect/HQ/...` 或 `Effect/Low/...` 并预加载。不要在 shared 引入 AssetDatabase、MVC 或资源包实现。
- 时间和事件连接由 `TimelineSchedule` 解析，包括片段结束绑定和全轴结束绑定；Spine 的 Normal 事件时间是动画原始秒数，解析时除以 Speed，Percent 则按片段时长计算。
- 技能演出只显示既有规则结果。当前实战协议没有命中序号，战斗适配允许零个或一个 PlayHit：一个时在命中点显示关联结果，零个时在演出结束显示。多个命中点会明确拒绝并回退，不能猜测多段结算的对应关系。

## 单位位移偏移与预览对象

`TimelineClipTranslateConfig.OffsetSpace` 只作用于单位位移，不改变特效、子弹或其他 PointData 的解释。

| 模式 | 语义 |
| --- | --- |
| ApproachDirection (0) | 新建位移默认值。先选最近/最远参考对象或求群体中心，再用进入时起点 S 到不带偏移的参考点 T 建地面坐标系；X 指向 T、Y 向上、Z=X×Y。终点为 T 加配置偏移，单位米，不受模型缩放影响。负 X 表示停在目标前。 |
| NodeLocal (1) | 偏移沿参考挂点的旋转与缩放；群体中心取首个参考的挂点朝向。 |
| World (2) | 相对参考点按世界 XYZ 方向加偏移。 |

`Destination.WorldPosition` 始终是绝对世界位置；返回初始位置使用真实初始点，不把世界位置按阵营翻转。起点与参考点重合时使用进入朝向，再退回世界 X。坐标系和落点进入片段时捕获，不随移动反复改变。编辑器终点手柄与运行时共用 `TimelineMoveOffsetUtil` 的正反转换。

预览键 `$caster` 表示自己，`$ally:n` 为相对友方的非施法者单位，`$enemy:n` 为相对敌方。阵营切换只重建实际对象，不改 JSON 内的敌我关系。预览仅接受相对角色键。实战始终使用协议真实对象，不读取这些预览键。

两端菜单 `Kabu/Tests/Timeline Movement Spaces` 验证双方近身、斜向、群体中心、进入快照、世界/局部空间、返回原点、手柄反变换、重合点及默认模式。乖乖虎 1.json 的近身与返回位移已显式使用 ApproachDirection；资源包需使用对应新版 JSON。

两端菜单 `Kabu/Tests/Shared Timeline Playback` 可检查位移采样、倒放重建、预览不发命中、大步进只发一次命中、资源回收、角色/相机复位和循环绑定拒绝。测试结果在各工程 `Temp/timeline-playback-checks.txt`。
