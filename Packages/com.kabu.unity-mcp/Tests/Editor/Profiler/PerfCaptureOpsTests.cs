using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers.Profiler;
using NUnit.Framework;

namespace KabuUnityMcp.Editor.Tests.Profiler
{
    public class PerfCaptureOpsTests
    {
        [Test]
        public void BuildBasics_NonZero_Ok()
        {
            var map = new Dictionary<string, long>
            {
                ["Batches Count"] = 109,
                ["Draw Calls Count"] = 114,
                ["SetPass Calls Count"] = 60,
                ["Triangles Count"] = 1000,
                ["Vertices Count"] = 2000
            };
            var section = PerfCaptureOps.BuildBasicsSection(map, strictBasics: true, out var ok);
            Assert.IsTrue(ok);
            Assert.IsNotNull(section);
            var data = section.GetType().GetProperty("data")?.GetValue(section);
            Assert.IsNotNull(data);
            var vertices = data.GetType().GetProperty("vertices")?.GetValue(data);
            Assert.AreEqual(2000L, vertices);
        }

        [Test]
        public void BuildBasics_AllZero_Strict_Fails()
        {
            var map = new Dictionary<string, long>
            {
                ["Batches Count"] = 0,
                ["Draw Calls Count"] = 0,
                ["SetPass Calls Count"] = 0
            };
            var section = PerfCaptureOps.BuildBasicsSection(map, strictBasics: true, out var ok);
            Assert.IsFalse(ok);
            Assert.IsNotNull(section);
        }

        [Test]
        public void BuildOverdraw_DelegatesToOverdrawOps()
        {
            var section = PerfCaptureOps.BuildOverdrawPlaceholder();
            Assert.IsNotNull(section);
            var status = section.GetType().GetProperty("status")?.GetValue(section) as string;
            Assert.IsFalse(string.IsNullOrEmpty(status));
        }

        [Test]
        public void ParseSections_Default_IncludesThree()
        {
            var set = PerfCaptureOps.ParseSections(null);
            Assert.IsTrue(set.Contains("basics"));
            Assert.IsTrue(set.Contains("profile"));
            Assert.IsTrue(set.Contains("frameDebugger"));
        }
    }
}
