using UnityEngine;

/// <summary>受力位移纯计算，编辑器预览与运行时 UnitForceControl 共用。</summary>
public static class TimelineForceMotionUtil
{
    public const float DefaultGravity = 20f;
    public const float HorizontalDeceleration = 30f;

    public static Vector3 ResolvePositionH(Vector3 startPos, Vector3 velocity, float time)
    {
        time = Mathf.Max(0f, time);

        float Displacement(float v0, float t)
        {
            float stopTime = Mathf.Abs(v0) / HorizontalDeceleration;
            float actualT = Mathf.Min(t, stopTime);
            return v0 * actualT - 0.5f * Mathf.Sign(v0) * HorizontalDeceleration * actualT * actualT;
        }

        return new Vector3(
            startPos.x + Displacement(velocity.x, time),
            startPos.y,
            startPos.z + Displacement(velocity.z, time));
    }

    public static Vector3 ResolvePosition(
        Vector3 startPos,
        Vector3 velocity,
        float time,
        float floorY,
        float gravity = DefaultGravity)
    {
        time = Mathf.Max(0f, time);

        var posY = startPos.y + velocity.y * time - 0.5f * gravity * time * time;
        if (posY <= floorY)
        {
            var landTime = CalculateLandTime(startPos.y, velocity.y, gravity, floorY);
            landTime = Mathf.Clamp(landTime, 0f, time);
            return new Vector3(
                startPos.x + velocity.x * landTime,
                floorY,
                startPos.z + velocity.z * landTime);
        }

        return new Vector3(
            startPos.x + velocity.x * time,
            posY,
            startPos.z + velocity.z * time);
    }

    private static float CalculateLandTime(float startY, float velocityY, float gravity, float floorY)
    {
        var a = -0.5f * gravity;
        var b = velocityY;
        var c = startY - floorY;

        var discriminant = b * b - 4f * a * c;
        if (discriminant < 0f)
        {
            return 0f;
        }

        var sqrtD = Mathf.Sqrt(discriminant);
        var t1 = (-b - sqrtD) / (2f * a);
        var t2 = (-b + sqrtD) / (2f * a);

        if (t1 > 0f && t2 > 0f)
        {
            return Mathf.Min(t1, t2);
        }

        if (t1 > 0f)
        {
            return t1;
        }

        if (t2 > 0f)
        {
            return t2;
        }

        return 0f;
    }
}
