using StardewMiningAgent.Models;

namespace StardewMiningAgent.Navigation;

public sealed class AStarPathfinder
{
    private readonly WorldState _state;
    private readonly RiskMap? _riskMap;
    private readonly float _riskWeight;
    private readonly int _maxNodes;
    private readonly float _moveCost;

    public AStarPathfinder(WorldState state, RiskMap? riskMap = null,
        float riskWeight = 2.0f, int maxNodes = 40000, float moveCost = 1.0f)
    {
        _state = state;
        _riskMap = riskMap;
        _riskWeight = riskWeight;
        _maxNodes = Math.Max(1, maxNodes);
        _moveCost = moveCost;
    }

    public PathResult FindPath(int startX, int startY, int goalX, int goalY)
    {
        var m = _state.Map;
        if (!m.InBounds(startX, startY) || !m.InBounds(goalX, goalY))
            return PathResult.Unreachable();

        if (startX == goalX && startY == goalY)
            return PathResult.Complete(new List<Vec2> { Vec2.FromTile(startX, startY) }, 0f);

        int W = m.Width, H = m.Height;
        int size = W * H;
        // Map sizes are data-driven; use heap arrays so large floors cannot overrun
        // an 8192-element stack buffer while initializing per-cell scores.
        var openSet = new int[Math.Min(size, _maxNodes)];
        int openCount = 0;

        var gScore = new float[size];
        var fScore = new float[size];
        var cameFrom = new int[size];
        for (int i = 0; i < size; i++) { gScore[i] = float.PositiveInfinity; fScore[i] = float.PositiveInfinity; cameFrom[i] = -1; }

        int startIdx = startY * W + startX;
        int goalIdx = goalY * W + goalX;
        gScore[startIdx] = 0;
        fScore[startIdx] = Heuristic(startX, startY, goalX, goalY);
        openSet[openCount++] = startIdx;

        Span<(int dx, int dy, float moveCost)> dirs = stackalloc (int, int, float)[8]
        {
            (0, -1, 1.0f), (0, 1, 1.0f), (-1, 0, 1.0f), (1, 0, 1.0f),
            (1, -1, 1.4142f), (-1, -1, 1.4142f), (1, 1, 1.4142f), (-1, 1, 1.4142f)
        };

        int nodesExpanded = 0;
        while (openCount > 0 && nodesExpanded < _maxNodes)
        {
            int bestIdx = 0;
            float bestF = fScore[openSet[0]];
            for (int i = 1; i < openCount; i++)
            {
                if (fScore[openSet[i]] < bestF) { bestF = fScore[openSet[i]]; bestIdx = i; }
            }
            int current = openSet[bestIdx];
            openSet[bestIdx] = openSet[--openCount];

            if (current == goalIdx)
            {
                return Reconstruct(cameFrom, current, W, H, gScore[goalIdx]);
            }

            int cx = current % W;
            int cy = current / W;
            nodesExpanded++;

            foreach (var (dx, dy, mc) in dirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!m.InBounds(nx, ny)) continue;
                if (!m.IsWalkable(nx, ny)) continue;
                if (dx != 0 && dy != 0)
                {
                    if (!m.IsWalkable(cx + dx, cy) || !m.IsWalkable(cx, cy + dy)) continue;
                }

                float riskCost = 0f;
                if (_riskMap != null) riskCost = _riskMap[nx, ny] * _riskWeight;
                float enemyCost = 0f;
                foreach (var e in _state.Enemies)
                {
                    float d = MathF.Sqrt((nx - e.Position.X) * (nx - e.Position.X) + (ny - e.Position.Y) * (ny - e.Position.Y));
                    float sigma = 1.5f;
                    enemyCost += MathF.Exp(-(d * d) / (2 * sigma * sigma)) * 0.8f;
                }

                int nIdx = ny * W + nx;
                float tentativeG = gScore[current] + mc * _moveCost + riskCost + enemyCost;
                if (tentativeG < gScore[nIdx])
                {
                    cameFrom[nIdx] = current;
                    gScore[nIdx] = tentativeG;
                    fScore[nIdx] = tentativeG + Heuristic(nx, ny, goalX, goalY);

                    bool inOpen = false;
                    for (int i = 0; i < openCount; i++) if (openSet[i] == nIdx) { inOpen = true; break; }
                    if (!inOpen)
                    {
                        if (openCount >= openSet.Length) return PathResult.Unreachable();
                        openSet[openCount++] = nIdx;
                    }
                }
            }
        }
        return PathResult.Unreachable();
    }

    private static float Heuristic(int ax, int ay, int bx, int by)
    {
        float dx = MathF.Abs(ax - bx);
        float dy = MathF.Abs(ay - by);
        return dx + dy + (1.4142f - 2f) * MathF.Min(dx, dy);
    }

    private static PathResult Reconstruct(int[] cameFrom, int endIdx, int W, int H, float totalCost)
    {
        var path = new List<Vec2>();
        int cur = endIdx;
        while (cur >= 0)
        {
            int tx = cur % W, ty = cur / W;
            path.Add(new Vec2(tx + 0.5f, ty + 0.5f));
            cur = cameFrom[cur];
        }
        path.Reverse();
        return PathResult.Complete(path, totalCost);
    }
}

public sealed class PathResult
{
    public bool Success { get; private init; }
    public List<Vec2> Path { get; private init; } = new();
    public float TotalCost { get; private init; }
    public float Length { get; private init; }

    public static PathResult Complete(List<Vec2> path, float cost)
    {
        float len = 0;
        for (int i = 1; i < path.Count; i++) len += path[i].DistanceTo(path[i - 1]);
        return new PathResult { Success = true, Path = path, TotalCost = cost, Length = len };
    }

    public static PathResult Unreachable() => new() { Success = false };

    public Vec2? ClosestReachable(Vec2 target)
    {
        Vec2? best = null;
        float bestD = float.MaxValue;
        foreach (var p in Path)
        {
            var d = p.DistanceTo(target);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    public PathResult Simplify(float minPointSeparation = 0.6f)
    {
        if (!Success || Path.Count <= 2) return this;
        var simp = new List<Vec2> { Path[0] };
        for (int i = 1; i < Path.Count; i++)
        {
            if (Path[i].DistanceTo(simp[^1]) >= minPointSeparation) simp.Add(Path[i]);
        }
        if (simp[^1] != Path[^1]) simp.Add(Path[^1]);
        return Complete(simp, TotalCost);
    }
}
