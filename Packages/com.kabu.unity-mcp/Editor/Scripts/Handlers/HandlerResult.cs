namespace KabuUnityMcp.Editor.Handlers
{
    /// <summary>Handler / Ops 共用结果，便于单测不断言 WebSocket。</summary>
    internal readonly struct HandlerResult
    {
        public bool Ok { get; }
        public string Error { get; }
        public object Payload { get; }
        public int ErrorCode { get; }

        private HandlerResult(bool ok, string error, object payload, int errorCode)
        {
            Ok = ok;
            Error = error;
            Payload = payload;
            ErrorCode = errorCode;
        }

        public static HandlerResult Success(object payload) =>
            new HandlerResult(true, null, payload, 0);

        public static HandlerResult Fail(string error, int errorCode = -32000) =>
            new HandlerResult(false, error, null, errorCode);
    }
}
