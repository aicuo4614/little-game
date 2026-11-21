using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>FrameTimingManager 帧时摘要（CPU/GPU/主线程/渲染线程）。</summary>
    internal static class ProfilerFrameTimingOps
    {
        public static HandlerResult Get()
        {
#if UNITY_2022_2_OR_NEWER
            if (!FrameTimingManager.IsFeatureEnabled())
            {
                return HandlerResult.Fail(
                    "Frame Timing Stats 未启用。请在 Project Settings → Player → Other Settings 勾选 " +
                    "'Frame Timing Stats'，或使用 Development Build（始终启用）。");
            }
#endif

            FrameTimingManager.CaptureFrameTimings();
            var timings = new FrameTiming[1];
            var count = FrameTimingManager.GetLatestTimings(1, timings);
            if (count == 0)
            {
                return HandlerResult.Success(new
                {
                    message = "暂无帧时数据（需先跑若干帧，建议 Play Mode）。",
                    available = false
                });
            }

            var t = timings[0];
            return HandlerResult.Success(new
            {
                message = "已捕获帧时。",
                available = true,
                cpuFrameTimeMs = Round3(t.cpuFrameTime),
#if UNITY_2022_2_OR_NEWER
                cpuMainThreadFrameTimeMs = Round3(t.cpuMainThreadFrameTime),
                cpuMainThreadPresentWaitTimeMs = Round3(t.cpuMainThreadPresentWaitTime),
                cpuRenderThreadFrameTimeMs = Round3(t.cpuRenderThreadFrameTime),
#endif
                gpuFrameTimeMs = Round3(t.gpuFrameTime),
#if UNITY_2022_2_OR_NEWER
                frameStartTimestamp = t.frameStartTimestamp,
                firstSubmitTimestamp = t.firstSubmitTimestamp,
#endif
                cpuTimePresentCalled = t.cpuTimePresentCalled,
                cpuTimeFrameComplete = t.cpuTimeFrameComplete,
                heightScale = t.heightScale,
                widthScale = t.widthScale,
                syncInterval = t.syncInterval
            });
        }

        private static double Round3(double v) => System.Math.Round(v, 3);
    }
}
