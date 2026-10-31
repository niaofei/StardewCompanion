namespace StardewCompanion.Protocol;

public readonly struct GridPoint : IEquatable<GridPoint>
{
    public int X { get; }
    public int Y { get; }
    public GridPoint(int x, int y) { X = x; Y = y; }
    public bool Equals(GridPoint other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is GridPoint point && Equals(point);
    public override int GetHashCode() => unchecked(X * 397 ^ Y);
    public int Distance(GridPoint other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    public IEnumerable<GridPoint> Neighbors()
    { yield return new(X, Y - 1); yield return new(X + 1, Y); yield return new(X, Y + 1); yield return new(X - 1, Y); }
}

public static class Pathfinding
{
    // A bounded four-direction A*. Never infer walkability from an unknown tile.
    public static IReadOnlyList<GridPoint>? AStar(GridPoint start, GridPoint goal, Func<GridPoint, bool> passable, int maxNodes = 20000)
        => AStar(start, goal, p => passable(p) ? 1 : null, maxNodes);

    // Weighted variant: entryCost returns null for blocked cells and a positive
    // cost for traversable cells. This lets callers model deliberate obstacle
    // removal without teaching the generic pathfinder game-specific rules.
    public static IReadOnlyList<GridPoint>? AStar(GridPoint start, GridPoint goal, Func<GridPoint, int?> entryCost, int maxNodes = 20000)
    {
        var sequence = 0L;
        var open = new SortedSet<(int Score, long Order, GridPoint Point)>(Comparer<(int Score, long Order, GridPoint Point)>.Create((a, b) =>
        { var c = a.Score.CompareTo(b.Score); return c != 0 ? c : a.Order.CompareTo(b.Order); }));
        var costs = new Dictionary<GridPoint, int> { [start] = 0 };
        var parents = new Dictionary<GridPoint, GridPoint>();
        var closed = new HashSet<GridPoint>();
        open.Add((start.Distance(goal), sequence++, start));
        while (open.Count > 0 && closed.Count < maxNodes)
        {
            var item = open.Min; open.Remove(item);
            var current = item.Point;
            if (!closed.Add(current)) continue;
            if (current.Equals(goal))
            {
                var path = new List<GridPoint> { current };
                while (!current.Equals(start)) { current = parents[current]; path.Add(current); }
                path.Reverse(); return path;
            }
            foreach (var next in current.Neighbors())
            {
                if (closed.Contains(next)) continue;
                var stepCost = entryCost(next);
                if (stepCost is null or <= 0) continue;
                var cost = costs[current] + stepCost.Value;
                if (costs.TryGetValue(next, out var old) && old <= cost) continue;
                costs[next] = cost; parents[next] = current;
                open.Add((cost + next.Distance(goal), sequence++, next));
            }
        }
        return null;
    }

    public static IReadOnlyList<T>? Dijkstra<T>(string start, string goal, IEnumerable<T> edges,
        Func<T, string> from, Func<T, string> to, Func<T, double> cost)
    {
        var groups = edges.GroupBy(from).ToDictionary(g => g.Key, g => g.ToArray());
        var remaining = new HashSet<string>(groups.Keys.Concat(edges.Select(to))) { start, goal };
        var distances = new Dictionary<string, double> { [start] = 0 };
        var previous = new Dictionary<string, T>();
        while (remaining.Count > 0)
        {
            var current = remaining.OrderBy(n => distances.TryGetValue(n, out var d) ? d : double.PositiveInfinity).First();
            if (!distances.ContainsKey(current)) break;
            remaining.Remove(current);
            if (current == goal)
            {
                var result = new List<T>();
                while (current != start) { var edge = previous[current]; result.Add(edge); current = from(edge); }
                result.Reverse(); return result;
            }
            if (!groups.TryGetValue(current, out var next)) continue;
            foreach (var edge in next)
            {
                var weight = cost(edge);
                if (double.IsNaN(weight) || double.IsInfinity(weight) || weight < 0) continue;
                var destination = to(edge); var candidate = distances[current] + weight;
                if (!distances.TryGetValue(destination, out var old) || candidate < old)
                { distances[destination] = candidate; previous[destination] = edge; }
            }
        }
        return null;
    }
}
