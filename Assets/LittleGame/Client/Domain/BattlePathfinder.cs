using System;
using System.Collections.Generic;
using Roy_T.AStar.Graphs;
using Roy_T.AStar.Grids;
using Roy_T.AStar.Paths;
using Roy_T.AStar.Primitives;

namespace LittleGame.Combat
{
    /// <summary>Roy-T.AStar 适配：复用图，按本次单位身体空间过滤边；仅接受完整路径。</summary>
    internal sealed class BattlePathfinder
    {
        private const float MinX = -5.5f, MinZ = -3f, Cell = .25f;
        private const int Width = 49, Height = 25;
        private readonly Grid grid;
        private readonly PathFinder finder = new PathFinder();
        private readonly List<NavPoint> raw = new List<NavPoint>();
        private Func<NavPoint, NavPoint, bool> clear;

        public bool Find(NavPoint start, NavPoint goal, Func<NavPoint, NavPoint, bool> canMove, List<NavPoint> output)
        {
            output.Clear();
            clear = canMove;
            if (canMove(start, goal)) { output.Add(goal); return true; }
            var startNode = Nearest(start, p => canMove(start, p));
            var goalNode = Nearest(goal, p => canMove(p, goal));
            if (startNode == null || goalNode == null) return false;
            var path = finder.FindPath(startNode, goalNode, Velocity.FromMetersPerSecond(1), CanTraverse);
            if (path.Type != PathType.Complete) return false;
            raw.Clear(); raw.Add(ToPoint(startNode));
            foreach (var edge in path.Edges) raw.Add(ToPoint(edge.End));
            raw.Add(goal);
            // 安全拉直，而非会穿过阻挡的曲线平滑。
            var from = start;
            int cursor = 0;
            while (cursor < raw.Count)
            {
                int furthest = cursor;
                for (int i = raw.Count - 1; i >= cursor; i--)
                    if (canMove(from, raw[i])) { furthest = i; break; }
                if (!canMove(from, raw[furthest])) { output.Clear(); return false; }
                from = raw[furthest]; output.Add(from); cursor = furthest + 1;
            }
            return true;
        }

        private bool CanTraverse(IEdge edge) => clear(ToPoint(edge.Start), ToPoint(edge.End));
        private static NavPoint ToPoint(INode node) => new NavPoint(node.Position.X, node.Position.Y);

        private INode Nearest(NavPoint point, Func<NavPoint, bool> valid)
        {
            int cx = (int)Math.Round((point.X - MinX) / Cell), cz = (int)Math.Round((point.Z - MinZ) / Cell);
            INode best = null; float bestD = float.MaxValue;
            for (int x = Math.Max(0, cx - 2); x <= Math.Min(Width - 1, cx + 2); x++)
                for (int z = Math.Max(0, cz - 2); z <= Math.Min(Height - 1, cz + 2); z++)
                {
                    var node = grid.GetNode(new GridPosition(x, z));
                    // 图的坐标从0开始；映射到战场坐标只做一次，见构造初始化。
                    var p = ToPoint(node);
                    float dx = p.X - point.X, dz = p.Z - point.Z, d = dx * dx + dz * dz;
                    if (d < bestD && valid(p)) { bestD = d; best = node; }
                }
            return best;
        }

        public BattlePathfinder()
        {
            // 重建为世界坐标图，仍使用相同拓扑；边长保持不变。
            var nodes = new Node[Width, Height];
            for (int x = 0; x < Width; x++)
                for (int z = 0; z < Height; z++) nodes[x, z] = new Node(new Position(MinX + x * Cell, MinZ + z * Cell));
            for (int x = 0; x < Width; x++)
                for (int z = 0; z < Height; z++)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                            if ((dx != 0 || dz != 0) && x + dx >= 0 && x + dx < Width && z + dz >= 0 && z + dz < Height)
                                nodes[x, z].Connect(nodes[x + dx, z + dz], Velocity.FromMetersPerSecond(1));
            grid = Grid.CreateGridFrom2DArrayOfNodes(nodes);
        }
    }
}
