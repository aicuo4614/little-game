namespace KabuUnityMcp.Editor.Extensions
{
    /// <summary>
    /// 工程侧 Host（如技能编辑器）回给通用 Handler 的结果。
    /// 与内部 <c>HandlerResult</c> 同形，但对跨程序集公开。
    /// </summary>
    public readonly struct McpHostResult
    {
        public bool Ok { get; }
        public string Error { get; }
        public object Payload { get; }
        public int ErrorCode { get; }

        private McpHostResult(bool ok, string error, object payload, int errorCode)
        {
            Ok = ok;
            Error = error;
            Payload = payload;
            ErrorCode = errorCode;
        }

        public static McpHostResult Success(object payload) =>
            new McpHostResult(true, null, payload, 0);

        public static McpHostResult Fail(string error, int errorCode = -32000) =>
            new McpHostResult(false, error, null, errorCode);
    }
}
