using System;
using System.Net.Sockets;

namespace KabuUnityMcp.Editor.Utils
{
    /// <summary>
    /// Thread-safe TCP probe. Safe to call from background threads.
    /// </summary>
    internal static class McpPortProbe
    {
        public static bool IsPortOpen(string host, int port, int timeoutMs = 150)
        {
            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect(host, port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs));
                if (!success)
                    return false;
                client.EndConnect(result);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
