using KabuUnityMcp.Editor.Services;
using NUnit.Framework;
using UnityEngine;

namespace KabuUnityMcp.Editor.Tests.Console
{
    public class McpLogCollectorTests
    {
        [SetUp]
        public void SetUp()
        {
            McpLogCollector.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            McpLogCollector.Clear();
        }

        [Test]
        public void Query_AfterLog_ReturnsEntry()
        {
            Debug.Log("McpLogCollector_Test_Message");
            var logs = McpLogCollector.Query(50, "Log", false, 0);
            Assert.IsTrue(logs.Exists(e => e.Message != null && e.Message.Contains("McpLogCollector_Test_Message")));
        }

        [Test]
        public void Query_FilterWarning_ExcludesLog()
        {
            Debug.Log("McpLogCollector_OnlyLog");
            var logs = McpLogCollector.Query(50, "Warning", false, 1);
            Assert.IsFalse(logs.Exists(e => e.Message != null && e.Message.Contains("McpLogCollector_OnlyLog")));
        }

        [Test]
        public void Clear_EmptiesBuffer()
        {
            Debug.Log("McpLogCollector_ToClear");
            McpLogCollector.Clear();
            var logs = McpLogCollector.Query(50, null, false, 0);
            Assert.IsFalse(logs.Exists(e => e.Message != null && e.Message.Contains("McpLogCollector_ToClear")));
        }
    }
}
