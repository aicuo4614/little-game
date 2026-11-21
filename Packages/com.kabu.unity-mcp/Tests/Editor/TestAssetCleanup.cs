using UnityEditor;
using UnityEngine.TestTools;

namespace KabuUnityMcp.Editor.Tests
{
    /// <summary>
    /// 测试清理。删资源会走资源后处理，Cursor 的工程生成会重写 csproj；
    /// 编辑器正占着文件时会打 Sharing violation。这条 Exception 日志会让用例失败，清理时忽略。
    /// </summary>
    internal static class TestAssetCleanup
    {
        public static void Delete(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (!AssetDatabase.IsValidFolder(path) && AssetDatabase.LoadMainAssetAtPath(path) == null)
                return;

            IgnoringEditorSync(() => AssetDatabase.DeleteAsset(path));
        }

        public static void Refresh()
        {
            IgnoringEditorSync(() => AssetDatabase.Refresh());
        }

        private static void IgnoringEditorSync(System.Action action)
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                action();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
