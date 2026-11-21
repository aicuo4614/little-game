namespace KabuUnityMcp.Editor.Models
{
    internal readonly struct McpLaunchResult
    {
        public bool Ok { get; }
        public string Error { get; }

        public McpLaunchResult(bool ok, string error = null)
        {
            Ok = ok;
            Error = error;
        }

        public static McpLaunchResult Success() => new McpLaunchResult(true);
        public static McpLaunchResult Fail(string error) => new McpLaunchResult(false, error);
    }
}
