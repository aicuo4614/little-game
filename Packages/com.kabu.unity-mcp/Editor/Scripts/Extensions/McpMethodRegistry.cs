using System;
using System.Collections.Generic;
using KabuUnityMcp.Networking;
using Newtonsoft.Json.Linq;

namespace KabuUnityMcp.Editor.Extensions
{
    /// <summary>WS method 扩展注册表：包内 Handler 或工程侧可挂自定义 method，避免 Bridge switch 膨胀。</summary>
    public static class McpMethodRegistry
    {
        public delegate void RequestHandler(string requestId, JToken parameters, McpWebSocketClient client);

        private static readonly Dictionary<string, RequestHandler> Handlers =
            new Dictionary<string, RequestHandler>(StringComparer.Ordinal);

        public static void Register(string method, RequestHandler handler)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                throw new ArgumentException("method is required", nameof(method));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Handlers[method.Trim()] = handler;
        }

        public static void Unregister(string method)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                return;
            }

            Handlers.Remove(method.Trim());
        }

        public static bool TryDispatch(string method, string requestId, JToken parameters, McpWebSocketClient client)
        {
            if (string.IsNullOrEmpty(method) || !Handlers.TryGetValue(method, out var handler))
            {
                return false;
            }

            handler(requestId, parameters, client);
            return true;
        }
    }
}
