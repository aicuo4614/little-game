# kabu-unity-mcp

Unity Editor 的 MCP：**UPM 包 + 本机 .NET Server**。传输用 Streamable HTTP，Editor 与 Server 之间用 WebSocket。不需要 Docker。

0.2.0 新增同包内置的 Spine/FairyGUI 查询、组件序列化读取、Camera PNG 取证、异步 EditMode 测试和项目扩展入口。无需拆包或安装第三方依赖；业务适配继续放项目 Editor 程序集。完整参数、边界、注册示例和跨工程核对见 [扩展与诊断](Documentation~/extensions.md)。

MCP 客户端（Cursor 等）连本机 Server；Server 把工具调用转给已连接的 Unity Editor，在编辑器里改场景、资源和播放状态。

## 连接模型

```
MCP 客户端 × N  -- Streamable HTTP /mcp -->  .NET Server  -- WebSocket /ws -->  Unity Editor × M
```

| 一侧 | 是否支持多连接 | 说明 |
|------|----------------|------|
| MCP 客户端（Cursor / Codex 等） | **支持** | `/mcp` 无连接数上限；可同时挂多个客户端 |
| Unity Editor | **支持** | `/ws` 用 `ConcurrentDictionary` 管理；`unity_list_editors` / `unity_select_editor` 选目标 |

### 多 MCP 客户端行为

- **`unity_select_editor` 按客户端隔离**：A 选中的 Editor 不影响 B。隔离 key 优先 `Mcp-Session-Id`（`initialize` 握手客户端）；无 session 时用 HTTP 连接 id（keep-alive 下较稳定）；都拿不到则落在共享桶 `default`。
- **发往同一 Editor 的 WebSocket 写串行化**：每路连接有发送锁，避免多客户端并发 `SendAsync` 竞态。
- **工具开关（`enabledTools`）仍是进程级全局**：任意已连接 Editor 上报的偏好对所有 MCP 客户端生效。
- **场景副作用不互斥**：两边同时改同一场景 / 进 Play 仍会互相干扰；隔离的是「默认打哪台 Editor」，不是编辑器事务锁。

传输模式：`StatefulForInitializeClients`——带 `initialize` 的客户端有 session；`2026-07-28` 及以后的无 session 请求仍按 Stateless 处理。

一个 Unity 工程通常跑一个 Server 进程（端口见该工程 `settings.json`）。多工程（如 client `3827` + assets `3828`）是**多个 Server**，不是单进程多租户。

## 环境

- Unity **2021.3+**（示例工程为 **2022.3 LTS**，URP 14）
- [.NET 10 SDK](https://dotnet.microsoft.com/download)，且 `dotnet` 在 `PATH` 中  
  Editor 窗口用 `dotnet run` 启动 Server，不会自带运行时。

## 安装

克隆本仓库后，在 Unity 工程的 `Packages/manifest.json` 里加入本地包。路径相对于该工程的 `Packages` 目录：

```json
"com.kabu.unity-mcp": "file:../../kabu-unity-mcp"
```

仓库公开到 GitHub 之后，也可以改成 Git URL（Unity 会按包根目录的 `package.json` 安装）：

```json
"com.kabu.unity-mcp": "https://github.com/<owner>/<repo>.git"
```

用本仓库自带的示例工程时不必再写一遍：`TestProject~/Packages/manifest.json` 已经是 `"com.kabu.unity-mcp": "file:../.."`。

## 使用

1. 用 Unity 打开已安装本包的工程。
2. 菜单 **Kabu → kabu-unity-mcp → Server Window**。
3. **启动服务器**，再 **连接**。
4. 把 MCP 客户端指到 `http://127.0.0.1:3827/mcp`。

Cursor / AILearning 工程配置示例（`.cursor/mcp.json`）：

```json
{
  "mcpServers": {
    "kabu-unity-mcp": {
      "type": "http",
      "url": "http://127.0.0.1:3827/mcp"
    }
  }
}
```

Codex 配置示例（`~/.codex/config.toml` 或工程 `.codex/config.toml`）：

```toml
[features]
rmcp_client = true

[mcp_servers."kabu-unity-mcp"]
url = "http://127.0.0.1:3827/mcp"
```

等价 CLI：`codex mcp add kabu-unity-mcp --url http://127.0.0.1:3827/mcp`。

端口和工具开关写在当前 Unity 工程的 `Assets/kabu-unity-mcp/settings.json`（默认端口 **3827**，工具默认全开）。窗口有三个页：服务器、MCP 工具、配置。

也可以不通过 Editor 窗口，在仓库里手动起 Server（仍需 Editor 连上 `/ws` 之后，改场景的工具才会生效）：

```powershell
cd Server~
dotnet run
```

健康检查：`http://127.0.0.1:3827/health`。`unity_ping` 只检查 HTTP，不操作场景。

### 卡布技能编辑器

在含 TimelineEditor 的美术工程使用。Demo 美术工程的端口为 `3832`。开发联调可按上面的本地包示例接入同级 MCP 源码，发布时由美术工程更新所使用的 MCP 包版本。

1. 调用 `unity_skill_editor_open`，再用 `unity_skill_editor_catalog` 查询 Clip 类型、默认值、可写字段以及全部编排动作参数。
2. 调用 `unity_skill_editor_read_timeline` 读取文档版本 `revision`、Clip、事件、边和绑定。使用 `unity_skill_editor_list_actions` 查询空白区、Clip、事件及头尾节点的右键动作。
3. 调用 `unity_skill_editor_edit` 提交一组结构化动作。默认 `dryRun: true`，会返回拟议文档。设为 `false` 才写入当前编辑缓冲区。新增 Clip 和事件可传 `alias`，同批后续动作通过 `$alias` 引用它们。
4. 用 `unity_skill_editor_command` 执行新建、另存、撤销重做、查询资源与预览角色、实战兼容校验等命令。保存会写入美术工程的技能 JSON。发布实战资源仍由项目的资源构建流程完成。

例如，把一条运镜的枢轴 X 改成 1.5，先试算同一个批次。

```json
{
  "revision": "<read_timeline 返回的版本>",
  "operations": [
    { "action": "patch_clip", "key": "<CameraShot 的 ClipKey>", "patch": { "PivotOffset": { "x": 1.5 } } }
  ],
  "dryRun": true
}
```

技能编辑器变更需要同步更新 MCP，规则见美术工程的 `.cursor/rules/skill-editor-mcp.mdc`。

## 仓库结构

```
kabu-unity-mcp/
  package.json          # UPM 清单
  Runtime/ Editor/      # 打进 Unity 的包代码
  Tests/                # Editor 测试（Unity Test Framework）
  Server~/              # .NET MCP Server（目录名以 ~ 结尾，Unity 不导入）
  TestProject~/         # 仓库内 URP 示例工程（同样不会打进 UPM）
```

```
Editor/Scripts/
  UI/         窗口、菜单
  Services/   启停、状态、WebSocket Bridge
  Handlers/   真正改编辑器的实现
Runtime/Scripts/
  Networking/ McpWebSocketClient
Server~/
  Services/   UnityWebSocketHub（/ws）、McpClientScope（客户端隔离）
  Tools/      MCP 工具（Meta 不碰场景；其余转发到 Editor）
```

## 工具

名称与 Editor 里的目录一致。新工具在 `settings.json` 里缺省为开启。

| 分组 | 工具 |
|------|------|
| Meta | `unity_ping`、`unity_ws_status` |
| System | `unity_get_project_info`、`unity_get_play_mode_state`、`unity_enter_play_mode`、`unity_exit_play_mode`、`unity_step_play_mode_frame`、`unity_get_compilation_status`、`unity_trigger_script_compilation`、`unity_run_menu_item`、`unity_bring_editor_to_foreground`、`unity_list_editors`、`unity_select_editor` |
| GameObjects | `unity_list_scene_objects`、`unity_find_game_object`、`unity_create_game_object`、`unity_batch_create_game_objects`、`unity_create_game_object_in_scene`、`unity_destroy_game_object`、`unity_duplicate_game_object`、`unity_set_parent`、`unity_modify_game_object` |
| Scenes | `unity_list_opened_scenes`、`unity_get_scene_data`、`unity_open_scene`、`unity_save_scene`、`unity_get_active_scene`、`unity_list_scenes`、`unity_set_active_scene`、`unity_close_scene` |
| Assets | `unity_find_assets`、`unity_refresh_assets`、`unity_create_folder`、`unity_copy_asset`、`unity_move_asset`、`unity_delete_asset`、`unity_create_script`、`unity_create_asset` |
| Console | `unity_get_console_logs`、`unity_clear_console_logs` |
| Components | `unity_add_component`、`unity_remove_component`、`unity_set_component_field` |
| Prefabs | `unity_create_prefab`、`unity_instantiate_prefab`、`unity_get_prefab_info`、`unity_open_prefab`、`unity_save_prefab`、`unity_close_prefab_stage`、`unity_get_prefab_stage` |

多个 Editor 同时连接时，用 `unity_list_editors` / `unity_select_editor` 选择目标；`[选中]` 标记只对**当前 MCP 客户端**生效。

## 示例工程

见 [TestProject~/README.md](TestProject~/README.md)。用 Unity Hub 打开 `TestProject~/`，菜单应出现 **Kabu → kabu-unity-mcp**。

## 测试

示例工程的 `manifest.json` 已把 `com.kabu.unity-mcp` 标为 testable。在 Unity **Window → General → Test Runner** 里跑 Edit Mode 测试。


## Memory Profiler：Editor / 真机快照

测试入口：**Kabu → kabu-unity-mcp → Server Window → 测试 → Memory Profiler**。面板直接调用与 MCP 相同的采集服务，不需要先连接智能体。

1. 真机运行 **Development Build**，先在 Unity Profiler 中连接设备。Editor 也可作为测试目标。
2. 点击“查询目标 / 采集状态”，选择目标并点击“连接所选目标”。目标属于当前 Editor，多个 MCP 客户端共享。
3. 点击“采集所选目标 Memory Snapshot”。路径可留空；自定义路径必须是新的 `.snap` 文件，位于 **Editor 主机**。
4. 结果显示快照路径、大小、采集目标、flags、时间及警告；旁边的 `.snap.mcp.json` 保存这些信息。列表和文件对比会读取该信息。
5. 在 Memory Profiler 包中打开 `.snap` 查看对象、类型及引用链。采集使用引擎 API，不强制依赖分析包。

| MCP 工具 | 参数与行为 |
|---|---|
| `unity_profiler_memory_targets` | 不传参数查询目标与忙碌状态；传 `targetId` 选择目标 |
| `unity_profiler_memory_take_snapshot` | `snapshotPath` 可选；推荐传 `expectedTargetId`，目标不匹配时拒绝采集 |
| `unity_profiler_memory_list_snapshots` | 目录内按创建时间倒序分页，返回可用的采集元数据 |
| `unity_profiler_memory_compare_snapshots` | **仅文件元数据对比**，`analysisKind=fileMetadataOnly`；文件大小变化不代表对象内存增长或泄漏 |

采集参数：`captureFlags` 默认 `7`（ManagedObjects=1、NativeObjects=2、NativeAllocations=4）；NativeAllocationSites=8、NativeStackTraces=16 需要目标支持。`timeoutSeconds` 默认 180，范围 10–600。采集会短暂停顿目标进程。

原生采集无法取消：请求超时后仍保持忙碌，直到原生回调到达，期间拒绝重复采集及 MCP 目标切换。晚到的成功快照仍保存元数据，可以通过列表找回。Unity 自带窗口仍可能改变目标；检测到采集前后目标变化时会返回警告。

新增 EditMode 测试类：`KabuUnityMcp.Editor.Tests.Profiler.ProfilerMemorySnapshotTests`，覆盖参数、覆盖保护、分页、损坏元数据、文件对比范围、目标检查，以及一次原生 Editor 快照和目标元数据验证。可在现有测试面板运行全部测试，或在 Unity Test Runner 单独运行此类。


### Connection recovery

- Unity script reload reconnects only the Editor WebSocket. The external MCP server remains running.
- WebSocket handshakes, sends and registration acknowledgements have a 10-second timeout. Both peers expire silent connections after 60 seconds.
- Sends are serialized. Each connection owns its socket and cancellation scope; stale tasks and queued requests cannot take over a replacement connection.
- Reconnect uses exponential backoff with jitter, followed by continued background retries. Manual disconnect cancels pending startup and connection work.
- Editor instance identity survives script reload through SessionState. Explicit editor selection is retained while offline; requests never silently fall back to another editor. Multiple editors require explicit selection.
- Compiling/registering editors reject operations before dispatch. In-flight operations fail on disconnect and are never automatically replayed, because their outcome may be unknown.
- Registration requires `unity.registered` from the server. Update the Editor package and external server together; restart an older server before using the updated package.
- Servers launched by Unity write to `<Unity project>/Library/KabuUnityMcp/server.log`. File logging is owned by the server and survives Unity domain reload; output is no longer piped through Unity callbacks.

Run standalone WebSocket recovery checks with .NET 10:

```powershell
dotnet run --project Tests~/Reliability/Reliability.csproj
```

The suite covers registration, concurrent sends, compile gating, persistent selection, replacement races, disconnecting pending requests, repeated reconnection, and heartbeat/registration timeouts. Actual Unity compilation and Domain Reload should also be verified in a project using this package. A project referencing the Git package will not pick up a local checkout automatically.
