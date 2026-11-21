using System.IO;
using KabuUnityMcp.Editor.Handlers.Assets;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Tests.Assets
{
    public class AssetOpsTests
    {
        private const string TempRoot = "Assets/kabu-unity-mcp/_TestTemp";

        [TearDown]
        public void TearDown()
        {
            TestAssetCleanup.Delete(TempRoot);
            TestAssetCleanup.Refresh();
        }

        [Test]
        public void Find_MaxResultsZero_Fails()
        {
            var result = AssetOps.Find("t:Script", null, 0);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("maxResults", result.Error);
        }

        [Test]
        public void Find_Scripts_ReturnsSome()
        {
            var result = AssetOps.Find("t:Script", null, 5);
            Assert.IsTrue(result.Ok, result.Error);
        }

        [Test]
        public void IsValidFolderName_RejectsSlash()
        {
            Assert.IsFalse(AssetOps.IsValidFolderName("a/b", out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void CreateFolder_Copy_Move_Delete_RoundTrip()
        {
            if (!AssetDatabase.IsValidFolder("Assets/kabu-unity-mcp"))
                AssetDatabase.CreateFolder("Assets", "kabu-unity-mcp");

            var createRoot = AssetOps.CreateFolder("Assets/kabu-unity-mcp", "_TestTemp");
            Assert.IsTrue(createRoot.Ok, createRoot.Error);

            var createChild = AssetOps.CreateFolder(TempRoot, "Child");
            Assert.IsTrue(createChild.Ok, createChild.Error);

            var src = TempRoot + "/hello.txt";
            var full = Path.Combine(Application.dataPath, "kabu-unity-mcp", "_TestTemp", "hello.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, "mcp-test");
            AssetDatabase.ImportAsset(src);

            var copy = AssetOps.Copy(src, TempRoot + "/Child/hello_copy.txt");
            Assert.IsTrue(copy.Ok, copy.Error);

            var move = AssetOps.Move(
                TempRoot + "/Child/hello_copy.txt",
                TempRoot + "/Child/hello_moved.txt");
            Assert.IsTrue(move.Ok, move.Error);

            var del = AssetOps.Delete(TempRoot + "/Child/hello_moved.txt");
            Assert.IsTrue(del.Ok, del.Error);

            var missing = AssetOps.Delete(TempRoot + "/Child/hello_moved.txt");
            Assert.IsFalse(missing.Ok);
        }

        [Test]
        public void NormalizeAssetPath_TrimsAndSlashes()
        {
            Assert.AreEqual("Assets/Foo", AssetOps.NormalizeAssetPath(" Assets\\Foo\\ "));
            Assert.IsNull(AssetOps.NormalizeAssetPath("  "));
        }

        [Test]
        public void CreateScript_EmptyPath_Fails()
        {
            var result = AssetOps.CreateScript("", null);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("path", result.Error);
        }

        [Test]
        public void CreateScript_NotCs_Fails()
        {
            var result = AssetOps.CreateScript("Assets/kabu-unity-mcp/_TestTemp/foo.txt", null);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains(".cs", result.Error);
        }

        // 注意：不要在 EditMode 套件里真写 .cs + ImportAsset，
        // 否则会触发脚本编译 / Domain Reload，打断 Test Runner。

        [Test]
        public void CreateAsset_EmptyType_Fails()
        {
            var result = AssetOps.CreateAsset("Assets/kabu-unity-mcp/_TestTemp/x.mat", "", null, null);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("assetType", result.Error);
        }

        [Test]
        public void CreateAsset_WrongExtension_Fails()
        {
            var result = AssetOps.CreateAsset(
                "Assets/kabu-unity-mcp/_TestTemp/x.txt", "Material", null, null);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains(".mat", result.Error);
        }

        [Test]
        public void CreateAsset_Material_RoundTrip()
        {
            if (!AssetDatabase.IsValidFolder("Assets/kabu-unity-mcp"))
                AssetDatabase.CreateFolder("Assets", "kabu-unity-mcp");

            var path = TempRoot + "/McpTestMat.mat";
            TestAssetCleanup.Delete(path);

            var create = AssetOps.CreateAsset(path, "Material", null, "1,0,0,1");
            Assert.IsTrue(create.Ok, create.Error);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>(path));

            var again = AssetOps.CreateAsset(path, "Material", null, null);
            Assert.IsFalse(again.Ok);
        }
    }
}
