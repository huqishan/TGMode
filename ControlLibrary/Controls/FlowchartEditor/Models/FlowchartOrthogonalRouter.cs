using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ControlLibrary.Controls.FlowchartEditor.Models
{
    /// <summary>
    /// 根据节点障碍物生成 90 度正交折线路径。
    /// </summary>
    public static class FlowchartOrthogonalRouter
    {
        // 节点外扩距离，折线路径不能穿过这个外扩矩形。
        public const double DefaultClearance = 18;
        // 连线先从锚点向外走一小段，再开始路由，避免一出来就被自身节点挡住。
        public const double DefaultPortOffset = 26;

        private const double Epsilon = 0.001;
        // Point/Rect 是 double，统一四舍五入后再做字典 Key，避免浮点误差导致重复点。
        private const double Precision = 1000;
        // 距离相同或接近时，给转角一点惩罚，让路径更少拐弯。
        private const double TurnPenalty = 20;
        private const int NoDirection = 0;
        private const int HorizontalDirection = 1;
        private const int VerticalDirection = 2;

        /// <summary>
        /// 为两个节点锚点生成避开所有节点矩形的正交折线路径。
        /// </summary>
        public static IReadOnlyList<Point> Route(
            Point startPoint,
            FlowchartAnchor startAnchor,
            Point endPoint,
            FlowchartAnchor endAnchor,
            IEnumerable<Rect> nodeBounds,
            Rect workspaceBounds,
            double clearance = DefaultClearance,
            double portOffset = DefaultPortOffset)
        {
            // 起点/终点先按锚点方向向外偏移，实际寻路从偏移点到偏移点进行。
            double safePortOffset = Math.Max(portOffset, clearance + 2);
            Point startExit = ClampToWorkspace(MoveFromAnchor(startPoint, startAnchor, safePortOffset), workspaceBounds);
            Point endEntry = ClampToWorkspace(MoveFromAnchor(endPoint, endAnchor, safePortOffset), workspaceBounds);
            //、、//
            try
            {
                // 障碍物是所有节点矩形的外扩版，确保折线不会贴边或穿模。
                List<Rect> obstacles = BuildObstacles(nodeBounds, workspaceBounds, clearance);
                List<Point> body = FindRoute(startExit, endEntry, obstacles, workspaceBounds);

                if (body.Count == 0)
                {
                    // 极端拥挤或无可达路径时，仍然返回一条 90 度折线，避免 UI 创建连线失败。
                    body = CreateFallbackBody(startExit, endEntry, obstacles);
                }

                return ComposeRoute(startPoint, body, endPoint);
            }
            catch
            {
                // 路由失败不能影响控件交互，兜底返回最简单的正交折线。
                return ComposeRoute(startPoint, CreateFallbackBody(startExit, endEntry, Array.Empty<Rect>()), endPoint);
            }
        }

        /// <summary>
        /// 为拖拽预览生成路径。预览终点是鼠标位置，没有真实目标锚点。
        /// </summary>
        public static IReadOnlyList<Point> RouteToPoint(
            Point startPoint,
            FlowchartAnchor startAnchor,
            Point endPoint,
            IEnumerable<Rect> nodeBounds,
            Rect workspaceBounds,
            double clearance = DefaultClearance,
            double portOffset = DefaultPortOffset)
        {
            // 预览线没有目标锚点，按鼠标相对起点的位置推断一个“进入方向”。
            FlowchartAnchor inferredEndAnchor = InferEndAnchor(startPoint, endPoint);
            return Route(startPoint, startAnchor, endPoint, inferredEndAnchor, nodeBounds, workspaceBounds, clearance, portOffset);
        }

        private static FlowchartAnchor InferEndAnchor(Point startPoint, Point endPoint)
        {
            Vector delta = endPoint - startPoint;

            if (Math.Abs(delta.X) >= Math.Abs(delta.Y))
            {
                return delta.X >= 0 ? FlowchartAnchor.Left : FlowchartAnchor.Right;
            }

            return delta.Y >= 0 ? FlowchartAnchor.Top : FlowchartAnchor.Bottom;
        }

        private static List<Rect> BuildObstacles(IEnumerable<Rect> nodeBounds, Rect workspaceBounds, double clearance)
        {
            List<Rect> obstacles = new List<Rect>();

            foreach (Rect bounds in nodeBounds)
            {
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
                {
                    continue;
                }

                Rect expanded = bounds;
                expanded.Inflate(clearance, clearance);
                // 工作区外的部分没有意义，裁剪后可以减少后续候选点数量。
                expanded.Intersect(workspaceBounds);

                if (!expanded.IsEmpty && expanded.Width > Epsilon && expanded.Height > Epsilon)
                {
                    obstacles.Add(NormalizeRect(expanded));
                }
            }

            return obstacles;
        }

        private static List<Point> FindRoute(Point startPoint, Point endPoint, IReadOnlyList<Rect> obstacles, Rect workspaceBounds)
        {
            List<double> xValues = new List<double>();
            List<double> yValues = new List<double>();

            // 候选通道由起终点、工作区边界、每个障碍物边界组成。
            // 它们两两组合成网格点，再只连接水平/垂直可见点。
            AddCoordinate(xValues, workspaceBounds.Left);
            AddCoordinate(xValues, workspaceBounds.Right);
            AddCoordinate(xValues, startPoint.X);
            AddCoordinate(xValues, endPoint.X);

            AddCoordinate(yValues, workspaceBounds.Top);
            AddCoordinate(yValues, workspaceBounds.Bottom);
            AddCoordinate(yValues, startPoint.Y);
            AddCoordinate(yValues, endPoint.Y);

            foreach (Rect obstacle in obstacles)
            {
                AddCoordinate(xValues, obstacle.Left);
                AddCoordinate(xValues, obstacle.Right);
                AddCoordinate(yValues, obstacle.Top);
                AddCoordinate(yValues, obstacle.Bottom);
            }

            Dictionary<PointKey, int> indexesByPoint = new Dictionary<PointKey, int>();
            List<Point> points = new List<Point>();

            // 起终点必须加入图，即使它们正好落在障碍外扩区域里。
            int startIndex = AddPoint(startPoint, startPoint, endPoint, obstacles, workspaceBounds, indexesByPoint, points, force: true);
            int endIndex = AddPoint(endPoint, startPoint, endPoint, obstacles, workspaceBounds, indexesByPoint, points, force: true);

            foreach (double x in xValues)
            {
                foreach (double y in yValues)
                {
                    AddPoint(new Point(x, y), startPoint, endPoint, obstacles, workspaceBounds, indexesByPoint, points, force: false);
                }
            }

            List<List<int>> adjacency = BuildAdjacency(points, obstacles);
            // 在正交可见性图上搜索最短路径，路径自然只会包含水平/垂直线段。
            return SearchShortestPath(points, adjacency, startIndex, endIndex);
        }

        private static int AddPoint(
            Point point,
            Point startPoint,
            Point endPoint,
            IReadOnlyList<Rect> obstacles,
            Rect workspaceBounds,
            Dictionary<PointKey, int> indexesByPoint,
            List<Point> points,
            bool force)
        {
            Point normalizedPoint = NormalizePoint(ClampToWorkspace(point, workspaceBounds));
            PointKey key = PointKey.FromPoint(normalizedPoint);

            if (indexesByPoint.TryGetValue(key, out int existingIndex))
            {
                return existingIndex;
            }

            bool isEndpoint = AreClose(normalizedPoint, startPoint) || AreClose(normalizedPoint, endPoint);
            if (!force && !isEndpoint && IsPointInsideAnyObstacle(normalizedPoint, obstacles))
            {
                // 普通候选点不能在障碍物内部，否则后续会生成穿节点线段。
                return -1;
            }

            int index = points.Count;
            indexesByPoint[key] = index;
            points.Add(normalizedPoint);
            return index;
        }

        private static List<List<int>> BuildAdjacency(IReadOnlyList<Point> points, IReadOnlyList<Rect> obstacles)
        {
            List<List<int>> adjacency = Enumerable.Range(0, points.Count)
                .Select(_ => new List<int>())
                .ToList();

            // 同一行的点按 X 排序，同一列的点按 Y 排序，只尝试连接相邻可见点。
            Dictionary<long, List<int>> rows = new Dictionary<long, List<int>>();
            Dictionary<long, List<int>> columns = new Dictionary<long, List<int>>();

            for (int index = 0; index < points.Count; index++)
            {
                PointKey key = PointKey.FromPoint(points[index]);
                AddIndex(rows, key.Y, index);
                AddIndex(columns, key.X, index);
            }

            foreach (List<int> row in rows.Values)
            {
                row.Sort((left, right) => points[left].X.CompareTo(points[right].X));
                ConnectVisibleNeighbors(row, points, obstacles, adjacency);
            }

            foreach (List<int> column in columns.Values)
            {
                column.Sort((left, right) => points[left].Y.CompareTo(points[right].Y));
                ConnectVisibleNeighbors(column, points, obstacles, adjacency);
            }

            return adjacency;
        }

        private static void AddIndex(Dictionary<long, List<int>> groups, long key, int index)
        {
            if (!groups.TryGetValue(key, out List<int>? indexes))
            {
                indexes = new List<int>();
                groups[key] = indexes;
            }

            indexes.Add(index);
        }

        private static void ConnectVisibleNeighbors(
            IReadOnlyList<int> indexes,
            IReadOnlyList<Point> points,
            IReadOnlyList<Rect> obstacles,
            List<List<int>> adjacency)
        {
            for (int i = 0; i < indexes.Count - 1; i++)
            {
                int from = indexes[i];
                int to = indexes[i + 1];

                // 相邻点之间如果被障碍物挡住，就不能作为图的一条边。
                if (!IsSegmentClear(points[from], points[to], obstacles))
                {
                    continue;
                }

                adjacency[from].Add(to);
                adjacency[to].Add(from);
            }
        }

        private static List<Point> SearchShortestPath(
            IReadOnlyList<Point> points,
            IReadOnlyList<List<int>> adjacency,
            int startIndex,
            int endIndex)
        {
            if (startIndex < 0 || endIndex < 0)
            {
                return new List<Point>();
            }

            PriorityQueue<RouterState, double> queue = new PriorityQueue<RouterState, double>();
            Dictionary<RouterState, double> distances = new Dictionary<RouterState, double>();
            Dictionary<RouterState, RouterState> previous = new Dictionary<RouterState, RouterState>();
            RouterState startState = new RouterState(startIndex, NoDirection);

            distances[startState] = 0;
            queue.Enqueue(startState, 0);

            // 状态里包含“到达当前点时的方向”，这样才能给转弯加惩罚。
            RouterState? endState = null;

            while (queue.Count > 0)
            {
                RouterState current = queue.Dequeue();
                double currentDistance = distances[current];

                if (current.PointIndex == endIndex)
                {
                    endState = current;
                    break;
                }

                foreach (int neighborIndex in adjacency[current.PointIndex])
                {
                    Point currentPoint = points[current.PointIndex];
                    Point neighborPoint = points[neighborIndex];
                    int nextDirection = GetDirection(currentPoint, neighborPoint);

                    if (nextDirection == NoDirection)
                    {
                        continue;
                    }

                    double edgeCost = (neighborPoint - currentPoint).Length;
                    double turnCost = current.Direction != NoDirection && current.Direction != nextDirection
                        ? TurnPenalty
                        : 0;

                    // Dijkstra：距离越短越优；距离接近时，转角少的路径会更优。
                    RouterState next = new RouterState(neighborIndex, nextDirection);
                    double nextDistance = currentDistance + edgeCost + turnCost;

                    if (distances.TryGetValue(next, out double knownDistance) && nextDistance >= knownDistance - Epsilon)
                    {
                        continue;
                    }

                    distances[next] = nextDistance;
                    previous[next] = current;
                    queue.Enqueue(next, nextDistance);
                }
            }

            if (!endState.HasValue)
            {
                return new List<Point>();
            }

            List<Point> path = new List<Point>();
            RouterState cursor = endState.Value;

            // previous 字典保存的是反向链路，这里从终点一路回溯到起点。
            while (true)
            {
                path.Add(points[cursor.PointIndex]);

                if (!previous.TryGetValue(cursor, out RouterState prior))
                {
                    break;
                }

                cursor = prior;
            }

            path.Reverse();
            return Simplify(path);
        }

        private static List<Point> CreateFallbackBody(Point startPoint, Point endPoint, IReadOnlyList<Rect> obstacles)
        {
            // 兜底优先尝试最简单的两段折线：先横后竖。
            List<Point> horizontalFirst = Simplify(new[]
            {
                startPoint,
                new Point(endPoint.X, startPoint.Y),
                endPoint
            });

            if (IsPathClear(horizontalFirst, obstacles))
            {
                return horizontalFirst;
            }

            // 再尝试先竖后横。
            List<Point> verticalFirst = Simplify(new[]
            {
                startPoint,
                new Point(startPoint.X, endPoint.Y),
                endPoint
            });

            if (IsPathClear(verticalFirst, obstacles))
            {
                return verticalFirst;
            }

            // 最后返回三段折线，即使可能无法完全避障，也能保证连线仍然是 90 度。
            double middleX = Normalize((startPoint.X + endPoint.X) / 2);
            return Simplify(new[]
            {
                startPoint,
                new Point(middleX, startPoint.Y),
                new Point(middleX, endPoint.Y),
                endPoint
            });
        }

        private static bool IsPathClear(IReadOnlyList<Point> path, IReadOnlyList<Rect> obstacles)
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                if (!IsSegmentClear(path[i], path[i + 1], obstacles))
                {
                    return false;
                }
            }

            return true;
        }

        private static IReadOnlyList<Point> ComposeRoute(Point startPoint, IReadOnlyList<Point> body, Point endPoint)
        {
            // body 只包含偏移点之间的路线，最终路径要补回真实锚点。
            List<Point> route = new List<Point> { startPoint };
            route.AddRange(body);
            route.Add(endPoint);
            return Simplify(route);
        }

        private static bool IsSegmentClear(Point startPoint, Point endPoint, IReadOnlyList<Rect> obstacles)
        {
            if (AreClose(startPoint, endPoint))
            {
                return true;
            }

            bool isHorizontal = Math.Abs(startPoint.Y - endPoint.Y) < Epsilon;
            bool isVertical = Math.Abs(startPoint.X - endPoint.X) < Epsilon;

            if (!isHorizontal && !isVertical)
            {
                return false;
            }

            foreach (Rect obstacle in obstacles)
            {
                if (IsPointInsideRectInterior(startPoint, obstacle) || IsPointInsideRectInterior(endPoint, obstacle))
                {
                    // 起点/终点可能在自身节点外扩矩形内，这种情况允许线段从里面“走出来”。
                    continue;
                }

                if (isHorizontal)
                {
                    double y = startPoint.Y;
                    double left = Math.Min(startPoint.X, endPoint.X);
                    double right = Math.Max(startPoint.X, endPoint.X);

                    if (y > obstacle.Top + Epsilon &&
                        y < obstacle.Bottom - Epsilon &&
                        right > obstacle.Left + Epsilon &&
                        left < obstacle.Right - Epsilon)
                    {
                        return false;
                    }
                }
                else
                {
                    double x = startPoint.X;
                    double top = Math.Min(startPoint.Y, endPoint.Y);
                    double bottom = Math.Max(startPoint.Y, endPoint.Y);

                    if (x > obstacle.Left + Epsilon &&
                        x < obstacle.Right - Epsilon &&
                        bottom > obstacle.Top + Epsilon &&
                        top < obstacle.Bottom - Epsilon)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsPointInsideAnyObstacle(Point point, IReadOnlyList<Rect> obstacles)
        {
            return obstacles.Any(obstacle => IsPointInsideRectInterior(point, obstacle));
        }

        private static bool IsPointInsideRectInterior(Point point, Rect rect)
        {
            return point.X > rect.Left + Epsilon &&
                   point.X < rect.Right - Epsilon &&
                   point.Y > rect.Top + Epsilon &&
                   point.Y < rect.Bottom - Epsilon;
        }

        private static int GetDirection(Point startPoint, Point endPoint)
        {
            if (Math.Abs(startPoint.X - endPoint.X) < Epsilon && Math.Abs(startPoint.Y - endPoint.Y) >= Epsilon)
            {
                return VerticalDirection;
            }

            if (Math.Abs(startPoint.Y - endPoint.Y) < Epsilon && Math.Abs(startPoint.X - endPoint.X) >= Epsilon)
            {
                return HorizontalDirection;
            }

            return NoDirection;
        }

        private static Point MoveFromAnchor(Point point, FlowchartAnchor anchor, double distance)
        {
            // 锚点方向决定连线从节点哪一边“出来”或“进入”。
            Vector direction = anchor switch
            {
                FlowchartAnchor.Top => new Vector(0, -1),
                FlowchartAnchor.Right => new Vector(1, 0),
                FlowchartAnchor.Bottom => new Vector(0, 1),
                _ => new Vector(-1, 0)
            };

            return point + (direction * distance);
        }

        private static Point ClampToWorkspace(Point point, Rect workspaceBounds)
        {
            return new Point(
                Clamp(point.X, workspaceBounds.Left, workspaceBounds.Right),
                Clamp(point.Y, workspaceBounds.Top, workspaceBounds.Bottom));
        }

        private static Rect NormalizeRect(Rect rect)
        {
            return new Rect(
                Normalize(rect.X),
                Normalize(rect.Y),
                Normalize(rect.Width),
                Normalize(rect.Height));
        }

        private static Point NormalizePoint(Point point)
        {
            return new Point(Normalize(point.X), Normalize(point.Y));
        }

        private static double Normalize(double value)
        {
            return Math.Round(value * Precision) / Precision;
        }

        private static void AddCoordinate(List<double> coordinates, double value)
        {
            double normalized = Normalize(value);

            if (coordinates.Any(coordinate => Math.Abs(coordinate - normalized) < Epsilon))
            {
                return;
            }

            coordinates.Add(normalized);
        }

        private static List<Point> Simplify(IEnumerable<Point> points)
        {
            List<Point> route = new List<Point>();

            // 先去掉重复点。
            foreach (Point point in points.Select(NormalizePoint))
            {
                if (route.Count == 0 || !AreClose(route[^1], point))
                {
                    route.Add(point);
                }
            }

            bool removedPoint;
            do
            {
                removedPoint = false;

                for (int i = 1; i < route.Count - 1; i++)
                {
                    if (!AreCollinear(route[i - 1], route[i], route[i + 1]))
                    {
                        continue;
                    }

                    // 中间点和前后点共线时没有意义，删除后路径更短也更好看。
                    route.RemoveAt(i);
                    removedPoint = true;
                    break;
                }
            }
            while (removedPoint);

            return route;
        }

        private static bool AreCollinear(Point first, Point second, Point third)
        {
            return Math.Abs(first.X - second.X) < Epsilon && Math.Abs(second.X - third.X) < Epsilon ||
                   Math.Abs(first.Y - second.Y) < Epsilon && Math.Abs(second.Y - third.Y) < Epsilon;
        }

        private static bool AreClose(Point first, Point second)
        {
            return Math.Abs(first.X - second.X) < Epsilon && Math.Abs(first.Y - second.Y) < Epsilon;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private readonly struct RouterState : IEquatable<RouterState>
        {
            public RouterState(int pointIndex, int direction)
            {
                PointIndex = pointIndex;
                Direction = direction;
            }

            public int PointIndex { get; }
            public int Direction { get; }

            public bool Equals(RouterState other)
            {
                return PointIndex == other.PointIndex && Direction == other.Direction;
            }

            public override bool Equals(object? obj)
            {
                return obj is RouterState other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(PointIndex, Direction);
            }
        }

        private readonly struct PointKey : IEquatable<PointKey>
        {
            public PointKey(long x, long y)
            {
                X = x;
                Y = y;
            }

            public long X { get; }
            public long Y { get; }

            public static PointKey FromPoint(Point point)
            {
                return new PointKey(
                    (long)Math.Round(point.X * Precision),
                    (long)Math.Round(point.Y * Precision));
            }

            public bool Equals(PointKey other)
            {
                return X == other.X && Y == other.Y;
            }

            public override bool Equals(object? obj)
            {
                return obj is PointKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(X, Y);
            }
        }
    }
}
