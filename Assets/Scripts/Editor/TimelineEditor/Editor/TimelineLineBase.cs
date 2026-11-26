using UnityEditor;
using UnityEngine;

public class TimelineLineBase
{
    private const int SegmentCount = 24;
    private const float LineWidth = 4f;
    private const float TangentFactor = 0.35f;
    private const float MinTangentLength = 24f;
    private const float DirectionVectorThreshold = 8f;
    private static readonly Color LineColor = new Color(0.35f, 0.75f, 1f, 0.95f);

    public void Draw(Vector2 startPos, Vector2 startDir, Vector2 endPos, Vector2 endDir)
    {
        var chord = Vector2.Distance(startPos, endPos);
        var tangentLength = Mathf.Max(chord * TangentFactor, MinTangentLength);

        var p0 = startPos;
        var p1 = startPos + ResolveTangent(startDir, tangentLength);
        var p2 = endPos + ResolveTangent(endDir, tangentLength);
        var p3 = endPos;

        var points = new Vector3[SegmentCount + 1];
        for (var i = 0; i <= SegmentCount; i++)
        {
            var t = i / (float)SegmentCount;
            points[i] = EvaluateCubicBezier(p0, p1, p2, p3, t);
        }

        Handles.BeginGUI();
        Handles.color = LineColor;
        Handles.DrawAAPolyLine(LineWidth, points);
        Handles.EndGUI();
    }

    private static Vector2 ResolveTangent(Vector2 dir, float tangentLength)
    {
        if (dir.sqrMagnitude < 0.0001f)
        {
            return Vector2.zero;
        }

        if (dir.magnitude <= DirectionVectorThreshold)
        {
            return dir.normalized * tangentLength;
        }

        return dir;
    }

    private static Vector3 EvaluateCubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var u = 1f - t;
        var u2 = u * u;
        var u3 = u2 * u;
        var t2 = t * t;
        var t3 = t2 * t;
        var x = u3 * p0.x + 3f * u2 * t * p1.x + 3f * u * t2 * p2.x + t3 * p3.x;
        var y = u3 * p0.y + 3f * u2 * t * p1.y + 3f * u * t2 * p2.y + t3 * p3.y;
        return new Vector3(x, y, 0f);
    }
}
