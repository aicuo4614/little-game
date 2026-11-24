# TestProject~

本仓库内的 Unity 示例工程（**URP**），用来安装并试用包。通过 UPM 引用仓库根目录：

```json
"com.kabu.unity-mcp": "file:../.."
```

已配置：

- `com.unity.render-pipelines.universal` 14.0.12
- `Assets/Settings/` 下的 URP Pipeline / Renderer
- Graphics / Quality 指向 URP，Color Space = Linear
- Test Runner 可跑包内 Edit Mode 测试（`testables` 含 `com.kabu.unity-mcp`）

## 打开方式

用 Unity Hub 打开本目录 `TestProject~/`。建议 Unity **2022.3 LTS**。首次打开会生成 `Library/`（已 gitignore）。

菜单应出现：**Kabu → kabu-unity-mcp → Server Window**。启动服务器并连接后，MCP 地址为 `http://127.0.0.1:3827/mcp`（端口可在 `Assets/kabu-unity-mcp/settings.json` 修改）。

本机需要已安装 .NET 10 SDK，且 `dotnet` 在 `PATH` 中。
