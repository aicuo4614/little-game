using System;
using UnityEngine;

namespace LittleGame
{
    // 持续采样帧间隔。读取时才排序，避免采样本身引入每帧分配。
    public sealed class FrameMetrics : MonoBehaviour
    {
        public static FrameMetrics Current { get; private set; }
        private readonly float[] intervals = new float[240];
        private int count;
        private int cursor;
        private float warmup = 1;

        private void Awake() { Current = this; }
        private void Update()
        {
            if (warmup > 0) { warmup -= Time.unscaledDeltaTime; return; }
            intervals[cursor] = Time.unscaledDeltaTime * 1000;
            cursor = (cursor + 1) % intervals.Length;
            count = Mathf.Min(count + 1, intervals.Length);
        }

        public object Snapshot()
        {
            if (count == 0) return new { available = false };
            var sorted = new float[count];
            Array.Copy(intervals, sorted, count);
            Array.Sort(sorted);
            double total = 0;
            foreach (float interval in sorted) total += interval;
            double mean = total / count;
            return new
            {
                available = true, samples = count, meanFrameMs = mean,
                estimatedFps = mean > 0 ? 1000 / mean : 0,
                p95FrameMs = sorted[(int)((count - 1) * .95f)],
                worstFrameMs = sorted[count - 1],
                targetFrameRate = Application.targetFrameRate,
                width = Screen.width, height = Screen.height, platform = Application.platform.ToString(),
                note = Application.isEditor ? "编辑器帧间隔，包含同步与外部工具开销，不能替代手机测量。" : "手机实际帧间隔；开发包包含每0.5秒只读状态落盘开销，不是GPU计时。"
            };
        }
        private void OnDestroy() { if (Current == this) Current = null; }
    }
}
