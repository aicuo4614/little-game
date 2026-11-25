using System;
using System.Collections.Generic;

namespace LittleGame.Combat
{
    /// <summary>
    /// 战斗导航：攻击落点预占 → 直达/A*绕行 → 连续定速跟随 → 扫掠校验 → 阻挡等待。
    /// 不推挤站定单位，不把计划速度写作实际速度；所有坐标/动作均在规则固定步长推进。
    /// </summary>
    internal sealed class BattleNavigation
    {
        public struct AgentInput
        {
            public int Id, TargetId;
            public bool IsTroop, HoldPosition;
            public float X, Z, Radius, MaxSpeed, TargetX, TargetZ, TargetRadius, AttackReach;
            public BattleNavigationState State;
        }
        public struct AgentOutput
        {
            public int Id;
            public bool IsTroop, Waiting;
            public float NewX, NewZ, NewVx, NewVz;
        }
        private sealed class Body
        {
            public AgentInput Input;
            public NavPoint Start, End;
        }
        private struct Candidate { public NavPoint Point; public float Score; }
        private readonly BattlePathfinder pathfinder = new BattlePathfinder();
        private readonly List<Body> bodies = new List<Body>(32);
        private readonly List<Body> order = new List<Body>(32);
        private readonly List<Candidate> candidates = new List<Candidate>(48);
        private readonly List<NavPoint> scratchPath = new List<NavPoint>();
        private readonly List<BattleNavObstacle> obstacles;
        private Body querying;
        private float time;

        public BattleNavigation(List<BattleNavObstacle> staticObstacles = null)
        { obstacles = staticObstacles ?? new List<BattleNavObstacle>(); }

        public void Reset() { time = 0; bodies.Clear(); order.Clear(); }

        public void Step(IList<AgentInput> inputs, float dt, List<AgentOutput> outputs)
        {
            time += dt; outputs.Clear(); bodies.Clear(); order.Clear();
            for (int i = 0; i < inputs.Count; i++)
            {
                var a = inputs[i];
                if (a.State == null) throw new ArgumentException("导航必须提供单位运行态");
                var body = new Body { Input = a, Start = new NavPoint(a.X, a.Z), End = new NavPoint(a.X, a.Z) };
                bodies.Add(body); order.Add(body);
            }
            // 前方先走、后方跟随；等待时间补偿降低长期饥饿，ID只作确定性平局裁决。
            order.Sort((a, b) =>
            {
                float pa = Distance(a.Start, new NavPoint(a.Input.TargetX, a.Input.TargetZ)) - Math.Min(3f, a.Input.State.BlockedFor);
                float pb = Distance(b.Start, new NavPoint(b.Input.TargetX, b.Input.TargetZ)) - Math.Min(3f, b.Input.State.BlockedFor);
                int compare = pa.CompareTo(pb); return compare != 0 ? compare : a.Input.Id.CompareTo(b.Input.Id);
            });
            foreach (var body in order)
            {
                var a = body.Input; var state = a.State; querying = body;
                if (a.HoldPosition || a.TargetId == 0)
                {
                    state.Status = NavigationStatus.Holding; state.BlockReason = ""; state.BlockedFor = 0;
                    state.HasGoal = false; state.Path.Clear(); state.Cursor = 0;
                    Output(body, dt, false, outputs); continue;
                }
                if (state.TargetId != a.TargetId)
                {
                    state.Path.Clear(); state.HasGoal = false; state.RetryAt = 0; state.BlockedFor = 0;
                    state.TargetId = a.TargetId;
                    state.GoalAttempt = 0;
                }
                if (time < state.RetryAt)
                {
                    state.BlockedFor += dt; Output(body, dt, true, outputs); continue;
                }
                float movedTarget = Distance(new NavPoint(a.TargetX, a.TargetZ), new NavPoint(state.PlannedTargetX, state.PlannedTargetZ));
                bool needsPlan = !state.HasGoal || state.Cursor >= state.Path.Count || movedTarget > .4f;
                if (needsPlan && !Plan(body))
                {
                    Wait(body, dt, "no_reachable_attack_position", outputs); continue;
                }
                while (state.Cursor < state.Path.Count && Distance(body.Start, state.Path[state.Cursor]) < .025f) state.Cursor++;
                if (state.Cursor >= state.Path.Count)
                {
                    state.Status = NavigationStatus.Arrived; state.BlockReason = "";
                    Output(body, dt, false, outputs); continue;
                }
                var waypoint = state.Path[state.Cursor];
                float distance = Distance(body.Start, waypoint);
                float travel = Math.Min(a.MaxSpeed * dt, distance);
                var next = new NavPoint(body.Start.X + (waypoint.X - body.Start.X) * travel / distance,
                    body.Start.Z + (waypoint.Z - body.Start.Z) * travel / distance);
                if (!CanMove(body.Start, next, true))
                {
                    // 路径突然被占：当步即站住；短间隔重新选位/规划，避免每帧左右试探。
                    state.Path.Clear(); state.HasGoal = false;
                    Wait(body, dt, "passage_occupied", outputs); continue;
                }
                body.End = next;
                state.Status = NavigationStatus.Moving; state.BlockReason = ""; state.BlockedFor = 0;
                Output(body, dt, false, outputs);
            }
        }

        private bool Plan(Body body)
        {
            var a = body.Input; var s = a.State;
            candidates.Clear();
            float minRadius = a.Radius + a.TargetRadius + .01f;
            float radius = Math.Max(minRadius, a.AttackReach - (a.IsTroop ? .09f : .03f));
            if (radius > a.AttackReach - .005f) return false;
            float bearing = (float)Math.Atan2(a.Z - a.TargetZ, a.X - a.TargetX);
            for (int i = 0; i < 36; i++)
            {
                float angle = bearing + i * (float)(Math.PI * 2 / 36);
                var point = new NavPoint(a.TargetX + (float)Math.Cos(angle) * radius, a.TargetZ + (float)Math.Sin(angle) * radius);
                if (!Inside(point, a.IsTroop) || !CanStand(point)) continue;
                bool claimed = false;
                foreach (var other in bodies)
                {
                    if (other == body || other.Input.IsTroop != a.IsTroop || other.Input.HoldPosition) continue;
                    var claim = other.Input.State;
                    if (claim.HasGoal && claim.TargetId == a.TargetId && Distance(point, new NavPoint(claim.GoalX, claim.GoalZ)) < a.Radius + other.Input.Radius)
                    { claimed = true; break; }
                }
                if (claimed) continue;
                float score = Distance(body.Start, point);
                if (s.HasGoal) score += Distance(point, new NavPoint(s.GoalX, s.GoalZ)) * .15f;
                candidates.Add(new Candidate { Point = point, Score = score });
            }
            candidates.Sort((x, y) => x.Score.CompareTo(y.Score));
            int batch = s.GoalAttempt++ * 8;
            for (int i = 0; i < Math.Min(candidates.Count, 8); i++)
            {
                // 预算内分批尝试；一次失败不永久锁在最近的八个落点。
                var goal = candidates[(batch + i) % candidates.Count].Point;
                if (!pathfinder.Find(body.Start, goal, CanPlanSegment, scratchPath)) continue;
                s.Path.Clear(); s.Path.AddRange(scratchPath); s.Cursor = 0;
                s.GoalX = goal.X; s.GoalZ = goal.Z; s.HasGoal = true; s.Revision++;
                s.GoalAttempt = 0;
                s.PlannedTargetX = a.TargetX; s.PlannedTargetZ = a.TargetZ;
                return true;
            }
            s.HasGoal = false;
            return false;
        }

        private void Wait(Body body, float dt, string reason, List<AgentOutput> outputs)
        {
            var state = body.Input.State;
            state.Status = reason == "no_reachable_attack_position" ? NavigationStatus.NoPath : NavigationStatus.Waiting;
            state.BlockReason = reason; state.BlockedFor += dt;
            state.RetryAt = time + .25f + (body.Input.Id % 3) * .025f;
            Output(body, dt, true, outputs);
        }

        private static void Output(Body body, float dt, bool waiting, List<AgentOutput> outputs)
        {
            // 边界处理后再求实际速度，零位移必定输出零速度。
            body.End = new NavPoint(Clamp(body.End.X, body.Input.IsTroop ? -5.25f : -5.35f, body.Input.IsTroop ? 5.2f : 6.35f), Clamp(body.End.Z, -2.95f, 2.95f));
            outputs.Add(new AgentOutput { Id = body.Input.Id, IsTroop = body.Input.IsTroop, Waiting = waiting,
                NewX = body.End.X, NewZ = body.End.Z, NewVx = (body.End.X - body.Start.X) / dt, NewVz = (body.End.Z - body.Start.Z) / dt });
        }

        private bool CanStand(NavPoint point)
        {
            foreach (var other in bodies)
                if (other != querying && Distance(point, other.End) < querying.Input.Radius + other.Input.Radius + .005f) return false;
            return StaticClear(point, point);
        }

        private bool CanPlanSegment(NavPoint from, NavPoint to) => CanMove(from, to, false);

        private bool CanMove(NavPoint from, NavPoint to, bool executing)
        {
            var a = querying.Input;
            if (!Inside(to, a.IsTroop) || !StaticClear(from, to)) return false;
            foreach (var other in bodies)
            {
                if (other == querying) continue;
                float radius = a.Radius + other.Input.Radius - .012f;
                var startOther = executing ? other.Start : other.End;
                var relFrom = new NavPoint(from.X - startOther.X, from.Z - startOther.Z);
                var relTo = new NavPoint(to.X - other.End.X, to.Z - other.End.Z);
                float startD = Distance(relFrom, new NavPoint());
                float min = SegmentDistance(new NavPoint(), relFrom, relTo);
                if (startD < radius)
                {
                    // 布阵允许较紧、出生也可能重叠；只允许不加深穿插的离开动作，不推挤他人。
                    if (Distance(relTo, new NavPoint()) + .001f < startD || min + .001f < startD) return false;
                }
                else if (min < radius) return false;
            }
            return true;
        }

        private bool StaticClear(NavPoint from, NavPoint to)
        {
            float r = querying.Input.Radius;
            foreach (var obstacle in obstacles)
            {
                float lo = 0, hi = 1;
                if (Slab(from.X, to.X - from.X, obstacle.MinX - r, obstacle.MaxX + r, ref lo, ref hi)
                    && Slab(from.Z, to.Z - from.Z, obstacle.MinZ - r, obstacle.MaxZ + r, ref lo, ref hi)) return false;
            }
            return true;
        }

        private static bool Slab(float origin, float delta, float min, float max, ref float lo, ref float hi)
        {
            if (Math.Abs(delta) < .00001f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            if (a > b) { float t = a; a = b; b = t; }
            lo = Math.Max(lo, a); hi = Math.Min(hi, b); return lo <= hi;
        }
        private static bool Inside(NavPoint p, bool troop) => p.X >= (troop ? -5.25f : -5.35f) && p.X <= (troop ? 5.2f : 6.35f) && p.Z >= -2.95f && p.Z <= 2.95f;
        internal static float Distance(NavPoint a, NavPoint b) { float x = a.X - b.X, z = a.Z - b.Z; return (float)Math.Sqrt(x * x + z * z); }
        private static float SegmentDistance(NavPoint point, NavPoint a, NavPoint b)
        {
            float x = b.X - a.X, z = b.Z - a.Z, d = x * x + z * z;
            float t = d < .000001f ? 0 : Clamp(((point.X - a.X) * x + (point.Z - a.Z) * z) / d, 0, 1);
            return Distance(point, new NavPoint(a.X + x * t, a.Z + z * t));
        }
        private static float Clamp(float v, float min, float max) => Math.Max(min, Math.Min(max, v));
    }
}
