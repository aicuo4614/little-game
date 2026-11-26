using UnityEditor;
using UnityEngine;

/// <summary>水平正弦波浪线，用于表示 Clip 的 DelayTime 区间。</summary>
public class TimelineWaveBase
{
    private const int SegmentCount = 48;
    private const float LineWidth = 2f;
    private const float Amplitude = 5f;
    private const float WavesPerHundredPixels = 4f;
    private static readonly Color WaveColor = new Color(1f, 0.88f, 0.25f, 0.95f);

    public void Draw(Vector2 startPos, Vector2 endPos)
    {
        var width = endPos.x - startPos.x;
        if (width <= 1f)
        {
            return;
        }

        var centerY = (startPos.y + endPos.y) * 0.5f;
        var waveCount = Mathf.Max(1f, width / 100f * WavesPerHundredPixels);
        var points = new Vector3[SegmentCount + 1];
        for (var i = 0; i <= SegmentCount; i++)
        {
            var t = i / (float)SegmentCount;
            var x = Mathf.Lerp(startPos.x, endPos.x, t);
            var phase = t * waveCount * Mathf.PI * 2f;
            var y = centerY + Mathf.Sin(phase) * Amplitude;
            points[i] = new Vector3(x, y, 0f);
        }

        Handles.BeginGUI();
        Handles.color = WaveColor;
        Handles.DrawAAPolyLine(LineWidth, points);
        Handles.EndGUI();
    }
}
