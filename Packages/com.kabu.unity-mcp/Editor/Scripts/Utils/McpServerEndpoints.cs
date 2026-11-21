using KabuUnityMcp.Editor.Services;

namespace KabuUnityMcp.Editor.Utils
{
    /// <summary>
    /// Editor endpoints from Assets/kabu-unity-mcp/settings.json.
    /// </summary>
    internal static class McpServerEndpoints
    {
        private static Models.McpLocalSettingsData Data => McpProjectSettingsStore.GetOrCreate();

        public static int Port => Data.port;
        public static string Host => string.IsNullOrWhiteSpace(Data.host) ? "127.0.0.1" : Data.host.Trim();
        public static string McpUrl => $"http://{Host}:{Port}/mcp";
        public static string HealthUrl => $"http://{Host}:{Port}/health";
        public static string BaseUrl => $"http://{Host}:{Port}";
        public static string WsUrl => $"ws://{Host}:{Port}/ws";

        public static void SetPort(int port) => McpProjectSettingsStore.SetPort(port);
    }
}
