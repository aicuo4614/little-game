---
name: mobile-test
description: >-
  Build, install, launch, and verify the Android development APK for little-game
  using Tools/Mobile/mobile_test.py. Use ONLY when the user explicitly requests
  building or installing to an Android phone ("装到手机", "手机验证", "打开手机游戏").
---

# Android 手机构建与真机验收

本工程 Android 开发测试包使用 IL2CPP ARM64，包名保留 `com.littlegame.teardown.dev`（展示名“符阵军团”）以支持原位更新并保留应用数据。

## 前置确认

- 核对根目录 `AGENTS.md` 当前轮次要求：若当前明确只需 Unity 编辑器验证，不得主动构建或安装手机包。
- 确认 Unity 编辑器已打开本工程且 `http://127.0.0.1:3833/mcp` 在线、无编译错误。

## 执行命令

- **默认流程（重新构建最新源码 → ADB 更新安装 → 自动启动 → 检查前台进程/截图/日志）**：
  ```powershell
  python -X utf8 Tools/Mobile/mobile_test.py
  ```
- **仅构建 APK（不安装）**：
  ```powershell
  python -X utf8 Tools/Mobile/mobile_test.py --build-only
  ```
- **复用已确认成功的 APK（仅在明确要求复用时使用）**：
  ```powershell
  python -X utf8 Tools/Mobile/mobile_test.py --install-built
  ```

## 约束与排障

- 构建失败时**严禁**安装旧包或声称新版本已在手机运行。
- 安装失败时保留原应用数据，不自动卸载或清空数据。
- 多台设备连接时必须显式指定目标设备，不得随意选择；首次 USB 调试授权、厂商安装确认或锁屏需向用户说明最小手动步骤。
- 构建状态与真机结果记录在 `Library/MobileTesting/build-status.json` 与 `Reports/Mobile/`；未连接设备或未完成真机检查的部分必须如实说明。
