# 修改日志

## 0.2.0

- 新增扩展目录和调用入口：项目声明操作契约，通用 Server 不引用项目类型；写操作显式开启，参数校验和错误结果统一返回。
- Spine、FairyGUI 查询按模块内置于同一个包，通过运行时检测兼容未安装依赖的工程，不拆包、不增加强制依赖。
- 支持 Spine 动画/骨骼/Skin/Slot/事件目录、播放轨道和骨骼坐标；FairyGUI 支持运行时组件树和控制器查询，不创建 UI。
- 新增组件序列化属性分页读取和 Camera PNG 取证；恢复临时渲染状态，输出路径限于当前工程 Library。
- 新增按程序集启动和查询 EditMode 测试任务，结果持久化到 Library；脚本重载后标记观察中断，不自动重跑。
- 新增无业务依赖的跨 Editor 扩展契约比较脚本，端点和 Editor ID 由调用方指定。
- 同步 Server 版本标识，并补充扩展契约、边界和验证方式文档。

## 0.1.2

- 补上 `CHANGELOG.md.meta`，消除 immutable 包目录里缺 meta 被忽略的警告

## 0.1.1

- 补上 `EditorConsoleReader.cs.meta`，让该脚本进入编辑器程序集，修复 `McpLogCollector` 的 CS0103

## 0.1.0

- `unity_get_console_logs` 改为读取 Unity Editor Console 当前条目，能读到已经显示的编译和导入警告
- 清空日志改为清编辑器 Console，不再只清开发者控制台
