using KabuUnityMcp.Editor.Handlers.SystemTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace KabuUnityMcp.Editor.Tests.SystemTools
{
    public class SystemOpsTests
    {
        [Test]
        public void GetProjectInfo_Succeeds()
        {
            var result = SystemOps.GetProjectInfo();
            Assert.IsTrue(result.Ok, result.Error);
        }

        [Test]
        public void GetPlayModeState_InEditMode_IsStopped()
        {
            Assume.That(!EditorApplication.isPlaying);
            var result = SystemOps.GetPlayModeState();
            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsFalse(EditorApplication.isPlaying);
        }

        [Test]
        public void ExitPlayMode_WhenStopped_Succeeds()
        {
            Assume.That(!EditorApplication.isPlaying);
            var result = SystemOps.ExitPlayMode();
            Assert.IsTrue(result.Ok, result.Error);
        }

        [Test]
        public void GetCompilationStatus_Succeeds()
        {
            var result = SystemOps.GetCompilationStatus();
            Assert.IsTrue(result.Ok, result.Error);
        }

        [Test]
        public void RunMenuItem_Empty_Fails()
        {
            var result = SystemOps.RunMenuItem("");
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void RunMenuItem_Invalid_Fails()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var result = SystemOps.RunMenuItem("No/Such/Menu/Item_McpZZZ");
                Assert.IsFalse(result.Ok);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void BringToForeground_Succeeds()
        {
            var result = SystemOps.BringToForeground();
            Assert.IsTrue(result.Ok, result.Error);
        }
    }
}
