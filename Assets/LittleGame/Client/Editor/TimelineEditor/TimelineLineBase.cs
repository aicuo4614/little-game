using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor.Timeline
{
    /// <summary>直接沿用 Demo TimelineLineBase 的三次贝塞尔蓝色连线。</summary>
    public sealed class TimelineLineBase
    {
        private const int SegmentCount = 24;
        private const float LineWidth = 4f;
        private const float TangentFactor = 0.35f;
        private const float MinTangentLength = 24f;
        private static readonly Color LineColor = new Color(0.35f, 0.75f, 1f, 0.95f);

        public void Draw(Vector2 startPos, Vector2 startDir, Vector2 endPos, Vector2 endDir)
        {
            float chord = Vector2.Distance(startPos, endPos);
            float tangentLength = Mathf.Max(chord * TangentFactor, MinTangentLength);

            Vector2 p0 = startPos;
            Vector2 p1 = startPos + startDir.normalized * tangentLength;
            Vector2 p2 = endPos + endDir.normalized * tangentLength;
            Vector2 p3 = endPos;

            var points = new Vector3[SegmentCount + 1];
            for (int i = 0; i <= SegmentCount; i++)
            {
                float t = i / (float)SegmentCount;
                points[i] = EvaluateCubicBezier(p0, p1, p2, p3, t);
            }

            Handles.BeginGUI();
            Color prev = Handles.color;
            Handles.color = LineColor;
            Handles.DrawAAPolyLine(LineWidth, points);
            Handles.color = prev;
            Handles.EndGUI();
        }

        private static Vector3 EvaluateCubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            float u2 = u * u;
            float u3 = u2 * u;
            float t2 = t * t;
            float t3 = t2 * t;
            float x = u3 * p0.x + 3f * u2 * t * p1.x + 3f * u * t2 * p2.x + t3 * p3.x;
            float y = u3 * p0.y + 3f * u2 * t * p1.y + 3f * u * t2 * p2.y + t3 * p3.y;
            return new Vector3(x, y, 0f);
        }
    }

    /// <summary>直接沿用 Demo TimelineWaveBase：48 段振幅 5px 的金黄正弦波浪线，表示 Clip 的 DelayTime 区间。</summary>
    public sealed class TimelineWaveBase
    {
        private const int SegmentCount = 48;
        private const float LineWidth = 2f;
        private const float Amplitude = 5f;
        private const float WavesPerHundredPixels = 4f;
        private static readonly Color WaveColor = new Color(1f, 0.88f, 0.25f, 0.95f);

        public void Draw(Vector2 startPos, Vector2 endPos)
        {
            float width = endPos.x - startPos.x;
            if (width <= 1f) return;

            float centerY = (startPos.y + endPos.y) * 0.5f;
            float waveCount = Mathf.Max(1f, width / 100f * WavesPerHundredPixels);
            var points = new Vector3[SegmentCount + 1];
            for (int i = 0; i <= SegmentCount; i++)
            {
                float t = i / (float)SegmentCount;
                float x = Mathf.Lerp(startPos.x, endPos.x, t);
                float phase = t * waveCount * Mathf.PI * 2f;
                float y = centerY + Mathf.Sin(phase) * Amplitude;
                points[i] = new Vector3(x, y, 0f);
            }

            Handles.BeginGUI();
            Color prev = Handles.color;
            Handles.color = WaveColor;
            Handles.DrawAAPolyLine(LineWidth, points);
            Handles.color = prev;
            Handles.EndGUI();
        }
    }
}
