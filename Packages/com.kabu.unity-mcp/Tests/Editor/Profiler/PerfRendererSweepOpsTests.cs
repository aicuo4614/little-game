using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers.Profiler;
using NUnit.Framework;

namespace KabuUnityMcp.Editor.Tests.Profiler
{
    public class PerfRendererSweepOpsTests
    {
        [Test]
        public void FromCounterMap_ReadsKnownNames()
        {
            var map = new Dictionary<string, long>
            {
                ["Batches Count"] = 109,
                ["Draw Calls Count"] = 114,
                ["SetPass Calls Count"] = 60,
                ["Triangles Count"] = 68177,
                ["Vertices Count"] = 1000
            };

            var m = PerfRendererSweepOps.FromCounterMap(map);
            Assert.AreEqual(109, m.Batches);
            Assert.AreEqual(114, m.DrawCalls);
            Assert.AreEqual(60, m.SetPassCalls);
        }

        [Test]
        public void BuildRow_ComputesBatchesDrop()
        {
            var batch = new PerfRendererSweepOps.BatchGroup
            {
                Index = 0,
                Label = "test",
                Items = new List<PerfRendererSweepOps.TargetItem>()
            };
            var before = new PerfRendererSweepOps.Metrics { Batches = 109 };
            var after = new PerfRendererSweepOps.Metrics { Batches = 90 };
            var row = PerfRendererSweepOps.BuildRow(batch, before, after);
            var delta = row.GetType().GetProperty("delta")?.GetValue(row);
            Assert.IsNotNull(delta);
            var drop = delta.GetType().GetProperty("batchesDrop")?.GetValue(delta);
            Assert.AreEqual(19L, drop);
        }
    }
}
