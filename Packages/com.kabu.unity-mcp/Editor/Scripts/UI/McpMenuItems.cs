using KabuUnityMcp;
using KabuUnityMcp.Editor.Utils;
using UnityEditor;

namespace KabuUnityMcp.Editor.UI
{
    public static class McpMenuItems
    {
        [MenuItem("Kabu/kabu-unity-mcp/About", priority = 100)]
        public static void About()
        {
            EditorUtility.DisplayDialog(
                PackageInfo.DisplayName,
                $"{PackageInfo.Name}\nVersion {PackageInfo.Version}\n\nServer: {McpServerEndpoints.McpUrl}\n启动/关闭：Kabu → kabu-unity-mcp → Server Window",
                "OK");
        }
    }
}
