# 扩展与诊断

所有模块随 `com.kabu.unity-mcp` 一个包发布。`Integrations/Spine`、`Integrations/FairyGui` 通过已加载类型进行检测，不增加包依赖，不要求项目安装第三方库。

## 依赖和代码归属

- `Server~/Tools/Unity/Diagnostics`：公开 MCP 参数、转发请求和错误。不引用 Unity 或项目业务类型。
- `Editor/Scripts/Handlers`：Unity 通用序列化读取、Camera 渲染和路由。
- `Editor/Scripts/Extensions`：公开的 `IMcpExtension`、操作描述和注册表。
- `Editor/Scripts/Integrations`：同包维护的可选库适配和测试入口。
- 项目 Editor 程序集：战斗角色身份、语义挂点、资源地址、技能预览/实战支持报告等适配。原业务模块决定规则，适配只查询和转换协议。不要向共享运行时程序集引入 MCP 依赖。

现有 SkillEditor/GPU Host 继续兼容，不必迁移或复制业务实现。扩展目录返回 Host 是否注册；这不代表所有技能片段或资源已经过实战验证。

## 工具

`unity_extension_catalog(extensionId?)` 返回当前选中 Editor 的版本、项目、Host 状态以及扩展目录。没有安装依赖的内置扩展仍可发现，`available=false` 并说明原因。

`unity_extension_call(extensionId, operation, arguments?, allowWrite=false)` 只调用已注册操作。注册表拒绝重复 ID，检查顶层必填、类型、枚举、未知参数；完整业务约束由适配实现校验。它不是完整 JSON Schema 校验器。不提供任意类型/成员名反射执行。写操作需要 `allowWrite=true`，只读操作仍由适配作者负责遵守契约。

### Spine

```json
{"extensionId":"spine","operation":"inspect","arguments":{"instanceId":123,"cameraInstanceId":456}}
{"extensionId":"spine","operation":"catalog","arguments":{"assetPath":"Assets/Character_SkeletonData.asset","kind":"animations","offset":0,"limit":50}}
{"extensionId":"spine","operation":"bones","arguments":{"instanceId":123,"boneName":"head"}}
```

- 场景查询选择 SkeletonAnimation/SkeletonRenderer/SkeletonGraphic **所在物体**，不自动猜测子物体；多个组件用 `componentIndex`。
- `catalog` 的 `kind` 支持 animations、skins、slots、bones、events。events 是事件定义目录，不是动画事件时间线。
- `inspect` 返回轨道、混合、速度、Skin、缩放/翻转、帧号；与相机的角度只是组件 Transform 的原始四元数夹角，不包含项目 billboard、父级方向规则。
- `bones` 返回当前已采样的骨骼位置，不推进动画、不更新骨架、不对缺失骨骼静默回退。骨骼世界坐标由组件 Transform 转换；项目自定义网格缩放/偏移需由项目适配解释。
- 未初始化场景骨架明确报错，不为了读取创建/初始化组件。`catalog(assetPath)` 可以加载 SkeletonDataAsset 数据，不创建场景对象。
- 不同 Spine 版本缺少的可选字段返回 null；无法识别的骨架/数据明确报错。未接入动画修改或 Spine 源工程编辑。

### FairyGUI

```json
{"extensionId":"fairygui","operation":"tree","arguments":{"uiPath":"0/2","depth":2,"offset":0,"limit":50}}
{"extensionId":"fairygui","operation":"controllers","arguments":{"uiPath":"0/2"}}
```

只读已有 GRoot；不通过 `GRoot.inst` 创建 Stage。路径是子项索引，可区分重名，但 UI 变化后需要重新查询。树查询最多 200 条、深度 6、文本 512 字符；虚拟列表只包含已实例化子项，不代表全部数据。控制器返回选中页和索引。不包含点击/编辑/发布；编辑器制作发布仍由 FairyGUI 编辑器桥接负责。

### 组件读取和画面取证

`unity_read_component(componentType, instanceId|path|name, componentIndex?, propertyPath?, offset?, limit?, maxDepth?)` 读取 Unity 序列化数据，含私有 SerializeField。不是任意公共属性 getter。容器通过子属性表示，未支持的叶子值为 null，同时保留 Unity 类型。上限 200 条、8 层、扫描 10000 项，达到扫描上限会标记 `scanLimitReached`。单条字符串最多返回 4096 字符，截断时 `valueTruncated=true`。

`unity_capture_camera(cameraInstanceId, width=1280, height=720)` 显式 Camera 渲染，PNG 写入当前工程 `Library/KabuUnityMcp/Captures` 的唯一文件，不接受任意输出路径、不覆盖文件。尺寸 16..2048，始终恢复 Camera 目标和当前 RenderTexture。会触发渲染回调；不是 GameView 截图，不含屏幕覆盖 UI。可先用现有技能 seek 等待采样完成，再查询快照和渲染；多次独立调用不承诺原子同帧。

### 测试任务

```json
{"extensionId":"tests","operation":"run","arguments":{"assemblyName":"MyProject.Editor.Tests","testName":"MyFixture.Example"},"allowWrite":true}
{"extensionId":"tests","operation":"status","arguments":{"runId":"run 返回的 ID"}}
```

仅运行显式指定程序集的 EditMode 测试，立即返回 runId。查询返回 queued/running/completed/failed/interrupted、计数、前 200 条失败及结果 JSON 路径。没有匹配用例也算失败。项目测试程序集必须已经加载，包测试需要 manifest 的 `testables`。只保留最近任务供查询，旧结果文件保留。

Domain Reload 后观察中断标记 interrupted，实际执行结果可能未知，不自动重跑。当前 Unity TestRunner API 不提供本入口所需取消保证，因此声明 `cancellationSupported=false`；不提供假取消或强行中止 Editor。

## 项目注册示例

以下类放在项目 Editor 程序集，而非 MCP 核心。`QueryExistingBattleDiagnostics()` 代表项目原有诊断接口，由项目实现。

```csharp
[InitializeOnLoad]
public static class ProjectMcpBootstrap
{
    private static ProjectBattleExtension extension = new ProjectBattleExtension();
    static ProjectMcpBootstrap()
    {
        McpExtensionRegistry.Register(extension);
        AssemblyReloadEvents.beforeAssemblyReload += () => McpExtensionRegistry.Unregister(extension);
    }
}

public sealed class ProjectBattleExtension : IMcpExtension
{
    public string Id => "project.battle";
    public string Version => "1.0.0";
    public string Description => "项目战斗诊断";
    public string UnavailableReason => null;
    public McpExtensionOperation[] Operations => new[]
    {
        new McpExtensionOperation("status", "读取项目战斗状态", true,
            JObject.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"))
    };
    public McpHostResult Execute(string operation, JObject arguments)
    {
        return McpHostResult.Success(QueryExistingBattleDiagnostics());
    }
}
```

项目应在不可用时返回真实原因，写操作自行维护 Undo/revision/恢复语义；注册表不会替项目保证事务或撤销。

## 跨工程核对

```powershell
python Tools~/compare_editors.py <美术MCP端点> <程序MCP端点>
```

同端点多个 Editor 时传 `--left-editor` / `--right-editor`，ID 来自 `unity_list_editors`。会话彼此隔离，不改其他客户端的选择。脚本比较包/Unity/扩展版本、可用状态、操作契约和 Host 状态。`contractsEqual` 仅表示契约相同，不推断资源存在、版本兼容或实战画面一致；这些证据应由项目扩展和项目测试提供。端点由调用方传入，不硬编码端口或目录。
