namespace KabuUnityMcp.Editor.Utils
{
    /// <summary>
    /// Builds client config snippets from <see cref="McpServerEndpoints"/>.
    /// </summary>
    internal static class McpClientConfigText
    {
        public static string BuildMcpJson(string serverKey = "kabu-unity-mcp")
        {
            return "{\n" +
                   "  \"mcpServers\": {\n" +
                   $"    \"{serverKey}\": {{\n" +
                   $"      \"url\": \"{McpServerEndpoints.McpUrl}\"\n" +
                   "    }\n" +
                   "  }\n" +
                   "}";
        }

        /// <summary>
        /// Codex：写入 ~/.codex/config.toml，或工程内 .codex/config.toml（仅 trusted 项目）。
        /// </summary>
        public static string BuildCodexToml(string serverKey = "kabu-unity-mcp")
        {
            return $"[mcp_servers.\"{serverKey}\"]\n" +
                   $"url = \"{McpServerEndpoints.McpUrl}\"\n" +
                   "default_tools_approval_mode = \"approve\"\n";
        }
    }
}
