using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    public enum NavigationStatus { Holding, Moving, Waiting, NoPath, Arrived }

    public struct NavPoint
    {
        public float X, Z;
        public NavPoint(float x, float z) { X = x; Z = z; }
    }

    /// <summary>单波导航运行态。路径、占位、阻挡原因可观测；不写入长期存档。</summary>
    public sealed class BattleNavigationState
    {
        public NavigationStatus Status { get; internal set; }
        public string BlockReason { get; internal set; } = "";
        public float BlockedFor { get; internal set; }
        public float GoalX { get; internal set; }
        public float GoalZ { get; internal set; }
        public int Revision { get; internal set; }
        internal int TargetId, Cursor, GoalAttempt;
        internal float PlannedTargetX, PlannedTargetZ, RetryAt;
        internal bool HasGoal;
        internal readonly List<NavPoint> Path = new List<NavPoint>();

        public void SetMotion(NavigationStatus status, string reason, float blockedFor)
        { Status = status; BlockReason = reason; BlockedFor = blockedFor; }

        public void SetNativePath(int targetId, NavPoint goal, IList<NavPoint> corners)
        {
            TargetId = targetId; GoalX = goal.X; GoalZ = goal.Z; HasGoal = true; Cursor = 0; Revision++;
            Path.Clear(); for (int i = 0; i < corners.Count; i++) Path.Add(corners[i]);
        }
        public void SetRemainingPath(IList<NavPoint> corners)
        { Path.Clear(); for (int i = 0; i < corners.Count; i++) Path.Add(corners[i]); Cursor = 0; }

        public void ClearNativePath() { HasGoal = false; Path.Clear(); Cursor = 0; }

        internal void Reset()
        {
            Status = NavigationStatus.Holding; BlockReason = ""; BlockedFor = RetryAt = 0;
            TargetId = Cursor = Revision = GoalAttempt = 0; HasGoal = false; Path.Clear(); GoalX = GoalZ = 0;
        }

        public object Snapshot() => new
        {
            status = Status.ToString(), blockReason = BlockReason, blockedFor = Math.Round(BlockedFor, 3),
            goalX = Math.Round(GoalX, 3), goalZ = Math.Round(GoalZ, 3), revision = Revision,
            remainingCorners = Math.Max(0, Path.Count - Cursor),
            corners = Path.Skip(Cursor).Select(p => new { x = Math.Round(p.X, 3), z = Math.Round(p.Z, 3) }).ToArray()
        };
    }

    /// <summary>静态阻挡矩形，使用规则平面坐标；当前庭院地面未设置装饰物为障碍。</summary>
    internal struct BattleNavObstacle
    {
        public float MinX, MaxX, MinZ, MaxZ;
        public BattleNavObstacle(float minX, float maxX, float minZ, float maxZ)
        { MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ; }
    }
}
