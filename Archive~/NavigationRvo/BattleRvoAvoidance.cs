using System;
using System.Collections.Generic;

namespace LittleGame.Combat
{
    /// <summary>
    /// 轻量级单线程确定性 ORCA / RVO2 二维平面避障求解器。
    /// 算法源自开源 RVO2 Library C# (Copyright 2008 University of North Carolina at Chapel Hill, Apache License 2.0；
    /// 参考 E:\Project\AnimalBattle\Assets\RVO\RVO\src)。
    /// 针对本工程军团战特点做无全局单例、无多线程、100% 确定性封装，并支持“站定攻击单位高权重”与“微速死区阻尼”，
    /// 保留约束求解后的安全速度，避免拥挤时强制满速造成穿插与往返修正。
    /// </summary>
    internal sealed class BattleRvoAvoidance
    {
        private const float RvoEpsilon = 0.00001f;

        private struct Vec2
        {
            public float X, Z;
            public Vec2(float x, float z) { X = x; Z = z; }
            public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Z + b.Z);
            public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Z - b.Z);
            public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Z * s);
            public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Z * s);
            public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Z / s);
            public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;
            public static float Det(Vec2 a, Vec2 b) => a.X * b.Z - a.Z * b.X;
            public float SqrLength() => X * X + Z * Z;
            public float Length() => (float)Math.Sqrt(X * X + Z * Z);
        }

        private struct OrcaLine
        {
            public Vec2 Point;
            public Vec2 Direction;
        }

        public struct AgentInput
        {
            public int Id;
            public bool IsTroop;
            public float X, Z;
            public float Vx, Vz;
            public float PrefVx, PrefVz;
            public float Radius;
            public float MaxSpeed;
            /// <summary>站定攻击/冻结/远程单位标记为高稳定权重，避免被路过单位轻易顶出射程引起抖动。</summary>
            public bool HoldPosition;
        }

        public struct AgentOutput
        {
            public int Id;
            public bool IsTroop;
            public float NewX, NewZ;
            public float NewVx, NewVz;
        }

        private readonly List<OrcaLine> lines = new List<OrcaLine>(32);

        public void Step(IList<AgentInput> agents, float dt, List<AgentOutput> outputs)
        {
            outputs.Clear();
            int count = agents.Count;
            if (count == 0) return;

            const float timeHorizon = 0.85f;
            const float neighborDist = 3.2f;
            float invTimeStep = 1.0f / Math.Max(0.01f, dt);

            for (int i = 0; i < count; i++)
            {
                var a = agents[i];
                var posA = new Vec2(a.X, a.Z);
                var velA = new Vec2(a.Vx, a.Vz);
                var prefVel = new Vec2(a.PrefVx, a.PrefVz);

                // 站定且无移动需求且未与其他单位严重穿插时，快速阻尼归零，杜绝原地颤抖
                if (a.HoldPosition && prefVel.SqrLength() < 1e-5f)
                {
                    bool overlapping = false;
                    Vec2 sepPush = new Vec2(0f, 0f);
                    for (int j = 0; j < count; j++)
                    {
                        if (i == j) continue;
                        var b = agents[j];
                        float dx = a.X - b.X;
                        float dz = a.Z - b.Z;
                        float minD = (a.Radius + b.Radius) * 0.92f;
                        float distSq = dx * dx + dz * dz;
                        if (distSq < minD * minD)
                        {
                            overlapping = true;
                            float dist = (float)Math.Sqrt(distSq);
                            float nx = dist > 1e-4f ? dx / dist : (i < j ? -1f : 1f);
                            float nz = dist > 1e-4f ? dz / dist : (i % 2 == 0 ? 1f : -1f);
                            float w = b.HoldPosition ? 0.5f : 0.25f;
                            float pen = (minD - dist) * w;
                            sepPush.X += nx * pen;
                            sepPush.Z += nz * pen;
                        }
                    }

                    if (!overlapping)
                    {
                        outputs.Add(new AgentOutput
                        {
                            Id = a.Id,
                            IsTroop = a.IsTroop,
                            NewX = a.X,
                            NewZ = a.Z,
                            NewVx = 0f,
                            NewVz = 0f
                        });
                        continue;
                    }

                    // 轻微穿插时用低速平滑解耦，不产生剧烈反弹
                    float pushLen = sepPush.Length();
                    float maxNudge = Math.Min(a.MaxSpeed * 0.45f * dt, 0.08f);
                    if (pushLen > maxNudge && pushLen > 1e-5f)
                        sepPush = sepPush * (maxNudge / pushLen);

                    outputs.Add(new AgentOutput
                    {
                        Id = a.Id,
                        IsTroop = a.IsTroop,
                        NewX = a.X + sepPush.X,
                        NewZ = a.Z + sepPush.Z,
                        NewVx = sepPush.X * invTimeStep * 0.5f,
                        NewVz = sepPush.Z * invTimeStep * 0.5f
                    });
                    continue;
                }

                lines.Clear();
                float invTimeHorizon = 1.0f / timeHorizon;

                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    var b = agents[j];
                    var relPos = new Vec2(b.X - a.X, b.Z - a.Z);
                    float distSq = relPos.SqrLength();
                    float combinedRadius = a.Radius + b.Radius;
                    if (distSq > (neighborDist + combinedRadius) * (neighborDist + combinedRadius)) continue;

                    var relVel = velA - new Vec2(b.Vx, b.Vz);
                    float combinedRadiusSq = combinedRadius * combinedRadius;

                    OrcaLine line;
                    Vec2 u;

                    if (distSq > combinedRadiusSq)
                    {
                        // 无碰撞：构造截断速度障碍圆锥的切线半平面
                        Vec2 w = relVel - invTimeHorizon * relPos;
                        float wLengthSq = w.SqrLength();
                        float dotProduct1 = Vec2.Dot(w, relPos);

                        if (dotProduct1 < 0.0f && dotProduct1 * dotProduct1 > combinedRadiusSq * wLengthSq)
                        {
                            float wLength = (float)Math.Sqrt(wLengthSq);
                            Vec2 unitW = wLength > RvoEpsilon ? w / wLength : new Vec2(1f, 0f);
                            line.Direction = new Vec2(unitW.Z, -unitW.X);
                            u = (combinedRadius * invTimeHorizon - wLength) * unitW;
                        }
                        else
                        {
                            float leg = (float)Math.Sqrt(Math.Max(0f, distSq - combinedRadiusSq));
                            if (Vec2.Det(relPos, w) > 0.0f)
                            {
                                line.Direction = new Vec2(
                                    relPos.X * leg - relPos.Z * combinedRadius,
                                    relPos.X * combinedRadius + relPos.Z * leg) / distSq;
                            }
                            else
                            {
                                line.Direction = -1f * new Vec2(
                                    relPos.X * leg + relPos.Z * combinedRadius,
                                    -relPos.X * combinedRadius + relPos.Z * leg) / distSq;
                            }
                            float dotProduct2 = Vec2.Dot(relVel, line.Direction);
                            u = dotProduct2 * line.Direction - relVel;
                        }
                    }
                    else
                    {
                        // 已发生重叠：按时间步平滑分离
                        Vec2 w = relVel - invTimeStep * relPos;
                        float wLength = w.Length();
                        Vec2 unitW = wLength > RvoEpsilon ? w / wLength : new Vec2(i < j ? -1f : 1f, i % 2 == 0 ? 0.2f : -0.2f);
                        line.Direction = new Vec2(unitW.Z, -unitW.X);
                        u = (combinedRadius * invTimeStep - wLength) * unitW;
                    }

                    // 责任分担系数：如果对方正在站定攻击，移动方承担更多绕行责任(0.85)，防止顶飞前排
                    // 站定分支不参与 ORCA 调速，因此移动方必须承担全部避让责任。
                    float responsibility = b.HoldPosition && !a.HoldPosition ? 1f : 0.5f;
                    line.Point = velA + responsibility * u;
                    lines.Add(line);
                }

                Vec2 newVel = prefVel;
                int lineFail = LinearProgram2(lines, a.MaxSpeed, prefVel, false, ref newVel);
                if (lineFail < lines.Count)
                {
                    LinearProgram3(lines, 0, lineFail, a.MaxSpeed, ref newVel);
                }

                // 不能归一化到满速，也不能截断低速：两者都会破坏 ORCA 半平面约束。
                // MaxSpeed 是速度上限；拥挤时允许自然减速，让行完成后再加速。
                Vec2 finalVel = newVel;

                outputs.Add(new AgentOutput
                {
                    Id = a.Id,
                    IsTroop = a.IsTroop,
                    NewX = a.X + finalVel.X * dt,
                    NewZ = a.Z + finalVel.Z * dt,
                    NewVx = finalVel.X,
                    NewVz = finalVel.Z
                });
            }
        }

        private static bool LinearProgram1(List<OrcaLine> lines, int lineNo, float radius, Vec2 optVelocity, bool directionOpt, ref Vec2 result)
        {
            float dotProduct = Vec2.Dot(lines[lineNo].Point, lines[lineNo].Direction);
            float discriminant = dotProduct * dotProduct + radius * radius - lines[lineNo].Point.SqrLength();
            if (discriminant < 0.0f) return false;

            float sqrtDiscriminant = (float)Math.Sqrt(discriminant);
            float tLeft = -dotProduct - sqrtDiscriminant;
            float tRight = -dotProduct + sqrtDiscriminant;

            for (int i = 0; i < lineNo; ++i)
            {
                float denominator = Vec2.Det(lines[lineNo].Direction, lines[i].Direction);
                float numerator = Vec2.Det(lines[i].Direction, lines[lineNo].Point - lines[i].Point);
                if (Math.Abs(denominator) <= RvoEpsilon)
                {
                    if (numerator < 0.0f) return false;
                    continue;
                }
                float t = numerator / denominator;
                if (denominator >= 0.0f) tRight = Math.Min(tRight, t);
                else tLeft = Math.Max(tLeft, t);
                if (tLeft > tRight) return false;
            }

            if (directionOpt)
            {
                result = Vec2.Dot(optVelocity, lines[lineNo].Direction) > 0.0f
                    ? lines[lineNo].Point + tRight * lines[lineNo].Direction
                    : lines[lineNo].Point + tLeft * lines[lineNo].Direction;
            }
            else
            {
                float t = Vec2.Dot(lines[lineNo].Direction, optVelocity - lines[lineNo].Point);
                if (t < tLeft) result = lines[lineNo].Point + tLeft * lines[lineNo].Direction;
                else if (t > tRight) result = lines[lineNo].Point + tRight * lines[lineNo].Direction;
                else result = lines[lineNo].Point + t * lines[lineNo].Direction;
            }
            return true;
        }

        private static int LinearProgram2(List<OrcaLine> lines, float radius, Vec2 optVelocity, bool directionOpt, ref Vec2 result)
        {
            if (directionOpt)
            {
                result = optVelocity * radius;
            }
            else if (optVelocity.SqrLength() > radius * radius)
            {
                float len = optVelocity.Length();
                result = len > RvoEpsilon ? (optVelocity / len) * radius : new Vec2(0f, 0f);
            }
            else
            {
                result = optVelocity;
            }

            for (int i = 0; i < lines.Count; ++i)
            {
                if (Vec2.Det(lines[i].Direction, lines[i].Point - result) > 0.0f)
                {
                    Vec2 tempResult = result;
                    if (!LinearProgram1(lines, i, radius, optVelocity, directionOpt, ref result))
                    {
                        result = tempResult;
                        return i;
                    }
                }
            }
            return lines.Count;
        }

        private static void LinearProgram3(List<OrcaLine> lines, int numObstLines, int beginLine, float radius, ref Vec2 result)
        {
            float distance = 0.0f;
            var projLines = new List<OrcaLine>(lines.Count);

            for (int i = beginLine; i < lines.Count; ++i)
            {
                if (Vec2.Det(lines[i].Direction, lines[i].Point - result) > distance)
                {
                    projLines.Clear();
                    for (int ii = 0; ii < numObstLines; ++ii) projLines.Add(lines[ii]);

                    for (int j = numObstLines; j < i; ++j)
                    {
                        OrcaLine line;
                        float determinant = Vec2.Det(lines[i].Direction, lines[j].Direction);
                        if (Math.Abs(determinant) <= RvoEpsilon)
                        {
                            if (Vec2.Dot(lines[i].Direction, lines[j].Direction) > 0.0f) continue;
                            line.Point = 0.5f * (lines[i].Point + lines[j].Point);
                        }
                        else
                        {
                            line.Point = lines[i].Point + (Vec2.Det(lines[j].Direction, lines[i].Point - lines[j].Point) / determinant) * lines[i].Direction;
                        }
                        Vec2 dir = lines[j].Direction - lines[i].Direction;
                        float len = dir.Length();
                        line.Direction = len > RvoEpsilon ? dir / len : new Vec2(1f, 0f);
                        projLines.Add(line);
                    }

                    Vec2 tempResult = result;
                    var optDir = new Vec2(-lines[i].Direction.Z, lines[i].Direction.X);
                    if (LinearProgram2(projLines, radius, optDir, true, ref result) < projLines.Count)
                    {
                        result = tempResult;
                    }
                    distance = Vec2.Det(lines[i].Direction, lines[i].Point - result);
                }
            }
        }
    }
}
