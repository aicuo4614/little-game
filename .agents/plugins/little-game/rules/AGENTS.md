# kabu-unity-mcp 包维护规则

以下规则仅在修改 `Packages/com.kabu.unity-mcp/` 内的文件时生效（迁移自 `Packages/com.kabu.unity-mcp/.cursor/rules/kabu-unity-mcp-dev.mdc`）：

- Package Manager 显示的版本号来自 `Packages/com.kabu.unity-mcp/package.json` 的 `version`，不能用 git 提交号代替。
- 用户能感知的改动（新工具、行为变化、修复）必须在同一次修改中同步完成：
  1. 提升 `Packages/com.kabu.unity-mcp/package.json` 的 `version`（修复加补丁号、新功能加次版本号、不兼容改动加主版本号）。
  2. 在 `Packages/com.kabu.unity-mcp/CHANGELOG.md` 顶部为该版本新增一节，用 `- ` 写清改了什么、为什么改。
  3. 确保 `version` 与 `CHANGELOG.md` 标题一致。
- 新增进包的文件（`.cs`、`.md` 等）必须配齐对应 `.meta` 文件，避免 `PackageCache` 忽略资源或引发 `CS0103`。
