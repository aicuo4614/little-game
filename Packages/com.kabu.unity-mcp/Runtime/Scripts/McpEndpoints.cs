namespace KabuUnityMcp
{
    /// <summary>
    /// Fallback defaults when project JSON is missing.
    /// </summary>
    public static class McpEndpoints
    {
        public const int DefaultPort = 3827;
        public const string DefaultHost = "127.0.0.1";

        public const int Port = DefaultPort;
        public const string Host = DefaultHost;

        public static string McpUrl => $"http://{Host}:{Port}/mcp";
        public static string HealthUrl => $"http://{Host}:{Port}/health";
        public static string BaseUrl => $"http://{Host}:{Port}";
        public static string WsUrl => $"ws://{Host}:{Port}/ws";
    }
}
