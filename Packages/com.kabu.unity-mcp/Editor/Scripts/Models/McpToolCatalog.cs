using System;
using System.Collections.Generic;
using KabuUnityMcp;

namespace KabuUnityMcp.Editor.Models
{
    [Serializable]
    internal class McpLocalSettingsData
    {
        public int port = McpEndpoints.DefaultPort;
        public string host = McpEndpoints.DefaultHost;
        /// <summary>
        /// Unity 编辑器冷启动时：若尚未连接，自动启服并连一次 WebSocket。
        /// Domain Reload / Play 切换仍走 WantConnected 重连，不受此项影响。
        /// 用可空：旧 JSON 缺字段时 Normalize 为 true，避免反序列化成 false。
        /// </summary>
        public bool? autoConnectOnEditorStart;
        /// <summary>tool name → enabled. Missing keys default to true.</summary>
        public Dictionary<string, bool> tools = new Dictionary<string, bool>();
    }

    internal readonly struct McpToolCatalogEntry
    {
        public string Name { get; }
        public string Category { get; }
        public string Description { get; }

        public McpToolCatalogEntry(string name, string category, string description)
        {
            Name = name;
            Category = category;
            Description = description;
        }
    }

    /// <summary>
    /// Known MCP tools (keep in sync with Server~/Tools). New tools default ON in JSON.
    /// </summary>
    internal static class McpToolCatalog
    {
        public static readonly McpToolCatalogEntry[] All =
        {
            new("unity_ping", "Meta", "健康检查：确认 MCP HTTP 是否通，不操作 Unity 场景。"),
            new("unity_ws_status", "Meta", "查询当前有多少 Unity Editor 通过 WebSocket 连上 Server。"),
            new("unity_extension_catalog", "Unity / Diagnostics", "查询可选集成与项目扩展的版本、参数和可用状态。"),
            new("unity_extension_call", "Unity / Diagnostics", "调用扩展操作；写操作需要显式 allowWrite。"),
            new("unity_combat_get_state", "Game / Combat", "军团远征状态、敌情、招募与胜负。"),
            new("unity_combat_get_view", "Game / Combat", "实际HUD、单位和阵位屏幕坐标。"),
            new("unity_combat_get_events", "Game / Combat", "按事件序号读取真实战斗事件。"),
            new("unity_combat_wait_for_event", "Game / Combat", "等待自然推进的战斗事件，不跳帧。"),
            new("unity_combat_restart", "Game / Combat", "正常重开可复现远征。"),
            new("unity_combat_spin", "Game / Combat", "正常横线招募和空转保底。"),
            new("unity_combat_deploy", "Game / Combat", "正常部署或交换军团阵位。"),
            new("unity_combat_merge", "Game / Combat", "三个同职业同星兵合成。"),
            new("unity_combat_get_definitions", "Game / Combat", "读取英雄、怪物、波次与招募池配表。"),
            new("unity_combat_start_wave", "Game / Combat", "正常开始当前波。"),
            new("unity_combat_next_wave", "Game / Combat", "波后进入下一波，不跳关。"),
            new("unity_combat_choose_reward", "Game / Combat", "正常选择局内强化。"),
            new("unity_combat_pointer", "Game / Combat", "玩家共用触屏入口与取消拖动复现。"),
            new("unity_read_component", "Unity / Components", "分页读取组件序列化属性和引用，不执行任意 getter。"),
            new("unity_capture_camera", "Unity / Diagnostics", "渲染指定 Camera 并输出 PNG（不含屏幕覆盖 UI）。"),

            new("unity_get_project_info", "Unity / System", "项目名、Unity 版本、路径、激活场景与 Play 状态。"),
            new("unity_get_play_mode_state", "Unity / System", "查询 Play Mode：Stopped / Playing / Paused。"),
            new("unity_enter_play_mode", "Unity / System", "进入 Play Mode；等到 Playing + Player 帧推进后返回（不保证有可画内容）；默认临时 DisableDomainReload。"),
            new("unity_exit_play_mode", "Unity / System", "退出 Play Mode，等到 Stopped 后返回。"),
            new("unity_step_play_mode_frame", "Unity / System", "Play Mode 单步下一帧（工具栏 ▶|❚❚ 旁按钮）；须已在 Play；步进后通常 Paused。"),
            new("unity_get_compilation_status", "Unity / System", "查询是否正在编译及最近编译是否成功。"),
            new("unity_trigger_script_compilation", "Unity / System", "请求重新编译脚本（可能 Domain Reload）。"),
            new("unity_run_menu_item", "Unity / System", "执行 Editor 菜单项（如 Assets/Refresh）。"),
            new("unity_bring_editor_to_foreground", "Unity / System", "尝试将 Unity Editor 窗口置于前台。"),
            new("unity_list_editors", "Unity / System", "列出已连接的 Unity Editor；[选中] 仅对本 MCP 客户端生效。"),
            new("unity_select_editor", "Unity / System", "为本 MCP 客户端选择后续工具请求的目标 Editor（与其他客户端隔离）。"),

            new("unity_list_scene_objects", "Unity / GameObjects", "列出当前激活场景的 GameObject 层级（含 path、instanceId），只读。"),
            new("unity_find_game_object", "Unity / GameObjects", "按 instanceId / 路径 / 名称查找 GameObject，返回详情与组件列表。"),
            new("unity_create_game_object", "Unity / GameObjects", "创建 GameObject，支持坐标、父物体、基础几何体，可撤销。"),
            new("unity_batch_create_game_objects", "Unity / GameObjects", "批量创建多个 GameObject。"),
            new("unity_create_game_object_in_scene", "Unity / GameObjects", "在指定场景中创建 GameObject（可 Additive 打开）。"),
            new("unity_destroy_game_object", "Unity / GameObjects", "销毁 GameObject（含子物体），可撤销。"),
            new("unity_duplicate_game_object", "Unity / GameObjects", "复制 GameObject（含层级与组件），可撤销。"),
            new("unity_set_parent", "Unity / GameObjects", "设置或解除 GameObject 的父物体，可撤销。"),
            new("unity_modify_game_object", "Unity / GameObjects", "修改名称、激活、tag、layer、位置/旋转/缩放等常用字段。"),

            new("unity_list_opened_scenes", "Unity / Scenes", "列出编辑器中已打开的场景，只读。"),
            new("unity_get_scene_data", "Unity / Scenes", "获取指定或激活场景的摘要与根物体层级，只读。"),
            new("unity_open_scene", "Unity / Scenes", "打开场景资源（Single 或 Additive）。"),
            new("unity_save_scene", "Unity / Scenes", "保存已打开场景；新场景需提供路径。"),
            new("unity_get_active_scene", "Unity / Scenes", "获取当前激活场景摘要。"),
            new("unity_list_scenes", "Unity / Scenes", "列出项目内全部场景资源路径。"),
            new("unity_set_active_scene", "Unity / Scenes", "将已打开场景设为激活场景。"),
            new("unity_close_scene", "Unity / Scenes", "关闭已打开场景（不可关闭唯一场景）。"),

            new("unity_find_assets", "Unity / Assets", "搜索项目资源（名称 / t:类型 / l:标签等）。"),
            new("unity_refresh_assets", "Unity / Assets", "刷新 AssetDatabase，外部改文件或写脚本后调用。"),
            new("unity_create_folder", "Unity / Assets", "在已存在的父路径下创建子文件夹。"),
            new("unity_copy_asset", "Unity / Assets", "复制项目内资源到新路径。"),
            new("unity_move_asset", "Unity / Assets", "移动或重命名项目内资源。"),
            new("unity_delete_asset", "Unity / Assets", "删除项目内资源（破坏性）。"),

            new("unity_get_console_logs", "Unity / Console", "读取 Editor Console 日志，可按类型与时间过滤。"),
            new("unity_clear_console_logs", "Unity / Console", "清空 Editor Console 与 MCP 日志缓冲。"),

            new("unity_add_component", "Unity / Components", "给 GameObject 添加组件（如 Rigidbody / 自定义脚本类名）。"),
            new("unity_remove_component", "Unity / Components", "从 GameObject 移除组件（不可移除 Transform）。"),
            new("unity_set_component_field", "Unity / Components", "设置组件公共字段/属性（int/float/bool/vector/color/asset 等）。"),

            new("unity_create_prefab", "Unity / Prefabs", "从场景物体创建 Prefab（可选变体），可撤销连接。"),
            new("unity_instantiate_prefab", "Unity / Prefabs", "将 Prefab 实例化到场景，可设坐标与父物体。"),
            new("unity_get_prefab_info", "Unity / Prefabs", "查询 Prefab 资源或场景实例的状态信息。"),
            new("unity_open_prefab", "Unity / Prefabs", "在 Prefab Mode（隔离）中打开 Prefab。"),
            new("unity_save_prefab", "Unity / Prefabs", "保存当前 Prefab Stage，或将场景实例覆盖应用到指定 Prefab。"),
            new("unity_close_prefab_stage", "Unity / Prefabs", "关闭 Prefab Mode，可选先保存。"),
            new("unity_get_prefab_stage", "Unity / Prefabs", "查询当前是否处于 Prefab Mode。"),

            new("unity_create_script", "Unity / Assets", "在 Assets 下创建 C# 脚本；内容为空则生成 MonoBehaviour 模板。"),
            new("unity_create_asset", "Unity / Assets", "创建 Material / Texture2D / ScriptableObject 等资源（勿用于 .cs）。"),

            new("unity_model_list_animation_clips", "Unity / Assets", "分页列出模型动画片段（Loop Time / Loop Pose）。只读。"),
            new("unity_model_get_animation_clip", "Unity / Assets", "读取一条模型动画的 Loop 与根运动导入参数。只读。"),
            new("unity_model_set_animation_clip", "Unity / Assets", "修改一条模型动画的 Loop / 根运动导入参数并重新导入。"),

            new("unity_frame_debugger_enable", "Unity / Profiler", "启用 Frame Debugger（打开窗口；Play 中会暂停）。"),
            new("unity_frame_debugger_disable", "Unity / Profiler", "关闭 Frame Debugger；若曾暂停则恢复播放。"),
            new("unity_frame_debugger_list", "Unity / Profiler", "分页列出当前帧事件目录（无 shader 详情）；可按类型/名称过滤。"),
            new("unity_frame_debugger_get_event", "Unity / Profiler", "按序号取单条完整事件（shader/pass/RT/状态/贴图等）；会移动回放游标。"),

            new("unity_profiler_enable", "Unity / Profiler", "启用 Unity Profiler（可选 areas/.raw/callstacks）；会增加开销，用完请 disable。"),
            new("unity_profiler_disable", "Unity / Profiler", "关闭 Profiler 与 binary log。"),
            new("unity_profiler_status", "Unity / Profiler", "查询 Profiler 开关、区域、已录帧范围。"),
            new("unity_profiler_set_areas", "Unity / Profiler", "按名称开关 ProfilerArea（CPU/GPU/Memory 等）。"),
            new("unity_profiler_clear", "Unity / Profiler", "清空已录制的 Profiler 帧。"),
            new("unity_profiler_get_frame_timing", "Unity / Profiler", "读取 FrameTimingManager 帧时（需开启 Frame Timing Stats）。"),
            new("unity_profiler_list_counters", "Unity / Profiler", "分页列出某 category 下 ProfilerRecorder 计数器名。"),
            new("unity_profiler_get_counters", "Unity / Profiler", "读取指定计数器（等 1～2 编辑器帧）；可省略 names 取前 40 个。"),
            new("unity_profiler_get_hierarchy", "Unity / Profiler", "取最近一帧 CPU Hierarchy Top N（self/total/gc）；禁止整树 dump。"),
            new("unity_profiler_get_hierarchy_children", "Unity / Profiler", "按 itemId 分页展开 Hierarchy 子节点。"),
            new("unity_profiler_get_object_memory", "Unity / Profiler", "查询场景物体或资源的运行时内存占用。"),
            new("unity_profiler_memory_targets", "Unity / Profiler", "List or select memory capture targets (Editor / Development Player); report capture availability and busy state."),
            new("unity_profiler_memory_take_snapshot", "Unity / Profiler", "异步拍摄 Memory Snapshot（.snap）；需 MemoryProfiler API。"),
            new("unity_profiler_memory_list_snapshots", "Unity / Profiler", "分页列出 .snap 文件。"),
            new("unity_profiler_memory_compare_snapshots", "Unity / Profiler", "对比两份 snapshot 的文件级摘要（不解析对象图）。"),

            new("unity_perf_capture_frame", "Unity / Profiler",
                "统一采集一帧性能摘要 JSON（schema kabu.perf.frame-capture/v1）：basics(Batches等)+profile(帧时/Hierarchy TopN)+frameDebugger目录摘要+overdraw占位。" +
                "编排：未 Play 则自动进 Play → 聚焦 Game → 活帧采 Counters → profile → Frame Debugger（会暂停）→ 默认恢复播放。" +
                "fdEventLimit 默认 0：不返回 topEvents（仅 eventCount+summaryByType）；>0 才按绘制顺序取前 N 条（非耗时排序）。" +
                "Game 需有画面；basics 全 0 会重试并可能 overall ok=false。细查仍用 get_counters / get_event / get_hierarchy_children。"),

            new("unity_perf_renderer_sweep", "Unity / Profiler",
                "按组临时 SetActive(false) 扫 Renderer 对 Batches 的贡献（schema kabu.perf.renderer-sweep/v1）。" +
                "关/恢复后按物体数加长等待，且连续 stableReads 次计数一致才采纳，避免脏帧。" +
                "groupBy=sibling|hierarchyChunk|parent；target=rendererGo|parent。默认 restore=true。"),

            new("unity_perf_get_overdraw", "Unity / Profiler",
                "Play/Game 过绘 A/B（schema kabu.perf.overdraw/v1）：整屏层数 + TopN avgLayersDrop（关掉物体后层数下降）。" +
                "透明优先候选；非真机 TBDR；可能自动进 Play并短暂禁用 Renderer。"),
            new("unity_get_occlusion_info", "Unity / Profiler",
                "只读遮挡剔除结论（schema kabu.perf.occlusion/v1）：Area/Portal、OcclusionCullingData、conclusion/gaps。"),
            new("unity_get_post_process_stack", "Unity / Profiler",
                "只读后处理组件盘点（schema kabu.perf.postprocess/v1）：Volume/PostProcess/Beautify 等按类型名匹配。"),
            new("unity_get_render_census", "Unity / Profiler",
                "只读渲染量级普查（schema kabu.perf.render-census/v1）：灯光分类与 Renderer/材质计数。"),

            new("unity_gpu_list_buffers", "Unity / Profiler",
                "分页列出已注册 GraphicsBuffer（schema kabu.gpu.buffer-list/v1）；默认刷新工程 GpuDebug Host。"),
            new("unity_gpu_read_buffer", "Unity / Profiler",
                "读取缓冲切片摘要（AsyncGPUReadback 或 CPU 镜像）；分页，禁止整缓冲 dump。"),

            new("unity_skill_editor_open", "Unity / SkillEditor", "打开技能编辑器场景与窗口（需工程注册 Host）。"),
            new("unity_skill_editor_status", "Unity / SkillEditor", "查询技能编辑器窗口、当前技能、播放/录制状态。"),
            new("unity_skill_editor_load", "Unity / SkillEditor", "按路径或 TimelineId 加载技能 JSON。"),
            new("unity_skill_editor_list_clips", "Unity / SkillEditor", "分页列出当前技能 Clip 摘要（key/type/时间）。"),
            new("unity_skill_editor_get_clip", "Unity / SkillEditor", "按 clipKey 取单条 Clip 配置摘要。"),
            new("unity_skill_editor_catalog", "Unity / SkillEditor", "查询技能类型、字段和操作契约。"),
            new("unity_skill_editor_read_timeline", "Unity / SkillEditor", "读取编辑缓冲区、连线和 revision。"),
            new("unity_skill_editor_list_actions", "Unity / SkillEditor", "读取各界面元素的上下文动作。"),
            new("unity_skill_editor_edit", "Unity / SkillEditor", "试算或提交一组 Timeline 编辑。"),
            new("unity_skill_editor_command", "Unity / SkillEditor", "执行文档、资源查询、选择和预览命令。"),
            new("unity_skill_editor_set_preview", "Unity / SkillEditor", "设置预览施法阵营 TeamA/TeamB 与画质 Low/HQ。"),
            new("unity_skill_editor_play", "Unity / SkillEditor", "预览播放；可 fromStart 从头播。"),
            new("unity_skill_editor_pause", "Unity / SkillEditor", "暂停预览播放。"),
            new("unity_skill_editor_stop", "Unity / SkillEditor", "停止预览并复位；若在录制则结束录制。"),
            new("unity_skill_editor_seek", "Unity / SkillEditor", "停播后滑动播放头到时间或 Clip 内容起点（不瞬移）。"),
            new("unity_skill_editor_snapshot", "Unity / SkillEditor", "采集当前帧单位/相机世界 TRS 快照。"),
            new("unity_skill_editor_record", "Unity / SkillEditor", "从头播放并采样预览录制 JSON（异步；默认停在内容终点，不硬跑 15s CutOff）。"),
        };
    }
}
