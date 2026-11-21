using System.Linq;
using UnityEditorInternal;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    internal static class ProfilerMemoryTargetOps
    {
        public static object Current() => new
        {
            targetId = ProfilerDriver.connectedProfiler,
            name = ProfilerDriver.GetConnectionIdentifier(ProfilerDriver.connectedProfiler)
        };

        public static HandlerResult List() => HandlerResult.Success(new
        {
            current = Current(),
            captureAvailable = ProfilerMemorySnapshotOps.Available,
            unavailableReason = ProfilerMemorySnapshotOps.UnavailableReason,
            captureBusy = ProfilerMemorySnapshotOps.IsBusy,
            defaultDirectory = ProfilerMemorySnapshotOps.DefaultDirectory,
            targets = ProfilerDriver.GetAvailableProfilers().Select(id => new
            {
                targetId = id,
                name = ProfilerDriver.GetConnectionIdentifier(id),
                selected = id == ProfilerDriver.connectedProfiler,
                connectable = ProfilerDriver.IsIdentifierConnectable(id)
            }).ToArray(),
            note = "Targets are shared by all clients of this Editor. For devices, connect a Development Build in the Unity Profiler first."
        });

        public static HandlerResult Select(int targetId)
        {
            if (ProfilerMemorySnapshotOps.IsBusy)
                return HandlerResult.Fail("A native memory capture is still running. Wait for its callback before switching targets.");
            if (!ProfilerDriver.GetAvailableProfilers().Contains(targetId))
                return HandlerResult.Fail("Target unavailable. Refresh targets and reconnect the Development Player.", -32602);
            if (targetId != ProfilerDriver.connectedProfiler && !ProfilerDriver.IsIdentifierConnectable(targetId))
                return HandlerResult.Fail("Target is not connectable.");
            ProfilerDriver.connectedProfiler = targetId;
            return ProfilerDriver.connectedProfiler == targetId
                ? HandlerResult.Success(Current())
                : HandlerResult.Fail("Unity did not select the requested target. Refresh targets.");
        }
    }
}
