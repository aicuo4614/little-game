using KabuUnityMcp.Editor.Services;
using UnityEngine;

namespace KabuUnityMcp.Editor.Extensions
{
    /// <summary>工程侧注册/注销 GPU 调试 Host。</summary>
    public static class McpGpuDebugHostRegistry
    {
        public static IMcpGpuDebugHost Current { get; private set; }

        public static bool HasHost => Current != null;

        public static void Register(IMcpGpuDebugHost host)
        {
            if (host == null)
            {
                throw new System.ArgumentNullException(nameof(host));
            }

            Current = host;
            Debug.Log("[kabu-unity-mcp] GpuDebug Host registered.");
            McpUnityBridge.PushToolPreferences();
        }

        public static void Unregister(IMcpGpuDebugHost host)
        {
            if (Current != host)
            {
                return;
            }

            Current = null;
            Debug.Log("[kabu-unity-mcp] GpuDebug Host unregistered.");
            McpUnityBridge.PushToolPreferences();
        }
    }
}
