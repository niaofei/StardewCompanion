using StardewMiningAgent.Models;

namespace StardewMiningAgent.Navigation;

public sealed class RiskMap
{
    private readonly int _width;
    private readonly int _height;
    private readonly float[] _risks;

    public RiskMap(int width, int height)
    {
        _width = width;
        _height = height;
        _risks = new float[width * height];
    }

    public int Width => _width;
    public int Height => _height;
    public float this[int tx, int ty]
    {
        get { if (tx < 0 || tx >= _width || ty < 0 || ty >= _height) return 1f; return _risks[ty * _width + tx]; }
        set { if (tx < 0 || tx >= _width || ty < 0 || ty >= _height) return; _risks[ty * _width + tx] = Math.Clamp(value, 0f, 1f); }
    }

    public void Clear()
    {
        for (int i = 0; i < _risks.Length; i++) _risks[i] = 0f;
    }

    public static RiskMap Compute(WorldState state, float playerHpRatioWeight = 1.0f)
    {
        var m = state.Map;
        var rm = new RiskMap(m.Width, m.Height);
        foreach (var e in state.Enemies)
        {
            float sigma = Math.Max(1.2f, e.AttackRange + 0.5f);
            float omega = 1f + (e.HasProjectile ? 0.3f : 0f);
            int cx = (int)e.Position.X;
            int cy = (int)e.Position.Y;
            var vel = e.Velocity;
            for (int y = 0; y < m.Height; y++)
            for (int x = 0; x < m.Width; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float d2 = dx * dx + dy * dy;
                float kernel = omega * MathF.Exp(-d2 / (2f * sigma * sigma));
                float dirBoost = 1f;
                if (Math.Abs(vel.X) + Math.Abs(vel.Y) > 0.1f)
                {
                    var vdir = new Vec2(vel.X, vel.Y).Normalized();
                    var rdir = new Vec2(dx, dy).Normalized();
                    var dot = vdir.X * rdir.X + vdir.Y * rdir.Y;
                    dirBoost = 1f + 0.4f * MathF.Max(0, dot);
                }
                rm[x, y] += kernel * dirBoost;
            }
        }
        var riskScale = 1f / MathF.Max(0.1f, state.Player.HpRatio);
        var scale = MathF.Min(5f, riskScale * playerHpRatioWeight);
        for (int i = 0; i < rm._risks.Length; i++)
            rm._risks[i] = MathF.Min(1f, rm._risks[i] * scale);
        return rm;
    }

    public float SafeDistance(Vec2 from)
    {
        float min = float.MaxValue;
        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width; x++)
        {
            if (_risks[y * _width + x] < 0.15f)
            {
                var d = from.DistanceTo(new Vec2(x + 0.5f, y + 0.5f));
                if (d < min) min = d;
            }
        }
        return min == float.MaxValue ? float.NaN : min;
    }

    public List<(int tx, int ty, float risk)> FindLowRiskNeighbors(int tx, int ty, float maxRisk = 0.3f)
    {
        var res = new List<(int, int, float)>();
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            if (Math.Abs(dx) + Math.Abs(dy) > 2) continue;
            var r = this[tx + dx, ty + dy];
            if (r <= maxRisk) res.Add((tx + dx, ty + dy, r));
        }
        return res;
    }
}
