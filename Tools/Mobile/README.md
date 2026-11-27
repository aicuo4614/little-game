# Android 手机验证

从工程根目录执行 `python -X utf8 Tools/Mobile/mobile_test.py`。默认编译当前热更 DLL、重建七个内置资源包、构建 Development / IL2CPP / ARM64 APK，然后 ADB 覆盖安装并启动，保留应用数据。多设备必须通过 `--serial` 指定；`--install-built` 只用于明确复用最近成功产物。

构建状态：`Library/MobileTesting/build-status.json`。安装、启动、截图、完整进程日志与结果：`Reports/Mobile/{时间}`。启动观察等待 12 秒，进程退出时也保存启动 PID 的完整日志；Unity Error、Android fatal 与 Unity 原生 CRASH 均视为失败。

开发包每 0.5 秒写入应用持久目录的 `observation.json`。当前格式为 `little-game.mobile-observation/v2`，包含基础战况、单位/敌人位置与生命、屏幕尺寸和诊断帧间隔。它不等同于完整 Editor MCP schema6：不包含完整技能实例、表现树、按钮坐标及 p95。手机持续采样不调用 `BattleSession.Snapshot()`；该完整匿名泛型快照在 Android 热更宿主已实测触发 SIGSEGV。Editor MCP 完整快照仍由原接口提供。

2026-11-27 黑屏复现、修复及真机验收见 `Reports/Mobile/black-screen-20261127`。手机时钟与电脑日期不同，报告目录以电脑时间命名，logcat 保留设备原始时间。
