using StardewMiningAgent.Models;

namespace StardewMiningAgent.Mining;

public readonly record struct OreValue(
    float Copper = 3f, float Iron = 5f, float Gold = 10f, float Iridium = 25f,
    float Special = 15f, float CoalPerPickup = 0.5f, float Stone = 0.2f, float Crate = 1.0f);

public sealed class OreEvaluator
{
    public OreValue Values { get; set; } = new();

    public float MarginalValue(TileKind kind, TargetResource target, int currentQty, int targetQty)
    {
        float baseV = kind switch
        {
            TileKind.Copper => Values.Copper,
            TileKind.Iron => Values.Iron,
            TileKind.Gold => Values.Gold,
            TileKind.Iridium => Values.Iridium,
            TileKind.Special => Values.Special,
            TileKind.Crate => Values.Crate,
            TileKind.Stone => Values.Stone,
            _ => 0f
        };
        if (targetQty <= 0) return baseV;
        float progress = Math.Clamp((float)currentQty / targetQty, 0f, 1f);
        float residual = kind switch
        {
            TileKind.Copper => 0.05f, TileKind.Iron => 0.05f, TileKind.Gold => 0.05f,
            TileKind.Iridium => 0.10f, TileKind.Special => 0.08f, TileKind.Crate => 0.02f,
            _ => 0f
        };
        float marginal = baseV * Math.Clamp(1f - progress, 0f, 1f) + baseV * residual;
        if (target == KindToTarget(kind)) marginal *= 1.0f;
        else marginal *= 0.3f;
        return marginal;
    }

    public static TargetResource KindToTarget(TileKind kind) => kind switch
    {
        TileKind.Copper => TargetResource.CopperOre,
        TileKind.Iron => TargetResource.IronOre,
        TileKind.Gold => TargetResource.GoldOre,
        TileKind.Iridium => TargetResource.IridiumOre,
        _ => TargetResource.Nothing
    };

    public int HammersNeeded(TileKind kind, float toolLevel) => kind switch
    {
        TileKind.Stone => 2,
        TileKind.Copper => 3,
        TileKind.Iron => 4,
        TileKind.Gold => 5,
        TileKind.Iridium => 6,
        TileKind.Special => 5,
        TileKind.Crate => 1,
        _ => 1
    };
}

public sealed class BombPlanner
{
    public float StoneValue { get; set; } = 0.5f;
    public float OreBonus { get; set; } = 2.0f;
    public float ExitBonus { get; set; } = 10f;
    public float RiskPenalty { get; set; } = 3.0f;
    public float TimePenalty { get; set; } = 2.0f;
    public float BombCostPenalty { get; set; } = 1.0f;

    public BombResult EvaluateBombPosition(
        WorldState state, int tileX, int tileY, BombKind bomb,
        InventoryState inv, TargetResource? target = null)
    {
        var m = state.Map;
        if (!m.InBounds(tileX, tileY)) return new BombResult(tileX, tileY, 0, 0, 0, 0, 0, 0, 0, float.MinValue, false, 0, "超出地图边界");

        int radius = bomb switch
        {
            BombKind.Small => 2,
            BombKind.Big => 3,
            BombKind.Dynamite => 3,
            _ => 2
        };

        int stoneCount = 0, oreCount = 0, exitLadderCount = 0, mineEntranceCount = 0;
        float oreValueEstimate = 0f;
        float riskAtBomb = 0f;
        var threatenedEnemyIds = new HashSet<int>();

        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int tx = tileX + dx, ty = tileY + dy;
            if (!m.InBounds(tx, ty)) continue;

            var t = m.Tiles[ty][tx];
            if (t == TileKind.Stone) stoneCount++;
            else if (t == TileKind.LadderDown) { exitLadderCount++; }
            else if (t == TileKind.MineEntrance) { mineEntranceCount++; }
            else if (t is TileKind.Copper or TileKind.Iron or TileKind.Gold or TileKind.Iridium or TileKind.Special or TileKind.Crate)
            {
                oreCount++;
                oreValueEstimate += t switch
                {
                    TileKind.Copper => 3f, TileKind.Iron => 5f, TileKind.Gold => 10f,
                    TileKind.Iridium => 25f, TileKind.Special => 15f, TileKind.Crate => 3f,
                    _ => 2f
                };
            }
            riskAtBomb += MathF.Exp(-(dx * dx + dy * dy) / (2f * 2f)) * 0.5f;
        }

        foreach (var e in state.Enemies)
        {
            float d = MathF.Sqrt((tileX - e.Position.X) * (tileX - e.Position.X)
                                + (tileY - e.Position.Y) * (tileY - e.Position.Y));
            if (d <= radius + 0.5f)
            {
                threatenedEnemyIds.Add(e.Id);
                riskAtBomb += 1f;
            }
        }

        bool playerInRange = false;
        float pd = state.Player.Position.DistanceTo(new Vec2(tileX, tileY));
        if (pd <= radius) playerInRange = true;

        float stoneGain = stoneCount * StoneValue;
        float oreGain = oreValueEstimate * OreBonus;
        float exitProb = exitLadderCount > 0 ? 1f : (mineEntranceCount > 0 ? 0.6f : 0f);
        float exitGain = exitProb * ExitBonus;

        float riskCost = riskAtBomb * RiskPenalty + (playerInRange ? 5f : 0f);
        float timeCost = 4f + MathF.Max(0, 8f - pd);
        float bombCost = 1f * BombCostPenalty;

        float totalGain = stoneGain + oreGain + exitGain;
        float totalCost = riskCost + timeCost * TimePenalty + bombCost;
        float bScore = totalGain - totalCost;

        return new BombResult(
            tileX, tileY, radius,
            stoneCount, oreCount, oreValueEstimate, exitProb,
            totalGain, totalCost, bScore,
            playerInRange, threatenedEnemyIds.Count,
            playerInRange ? "玩家在爆炸范围内" : null);
    }

    public record BombResult(
        int TileX, int TileY, int Radius,
        int StoneCount, int OreCount, float OreValueEstimate, float ExitProbability,
        float TotalGain, float TotalCost, float Score,
        bool PlayerHitByBomb, int EnemiesThreatened,
        string? BlockReason)
    {
        public bool Valid => BlockReason == null;
    }
}

public sealed class BlastSafety
{
    public bool CanEvacuateToSafeZone(WorldState state, Vec2 bombPos, float radius, Navigation.RiskMap? rm = null)
    {
        var m = state.Map;
        var startIdx = (state.Player.TileY - 1) * m.Width + state.Player.TileX;
        bool[,] visited = new bool[m.Width, m.Height];
        visited[state.Player.TileX, state.Player.TileY] = true;
        var queue = new Queue<(int, int, int)>();
        queue.Enqueue((state.Player.TileX, state.Player.TileY, 0));

        Span<(int dx, int dy)> dirs = stackalloc (int, int)[4]
        {
            (0, -1), (0, 1), (-1, 0), (1, 0)
        };

        while (queue.Count > 0)
        {
            var (x, y, steps) = queue.Dequeue();
            float dxB = x - bombPos.X;
            float dyB = y - bombPos.Y;
            float distB = MathF.Sqrt(dxB * dxB + dyB * dyB);
            if (distB > radius && steps <= 6) return true;
            if (steps > 6) continue;

            foreach (var (dx, dy) in dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (!m.InBounds(nx, ny) || visited[nx, ny]) continue;
                if (!m.IsWalkable(nx, ny)) continue;
                float edx = nx - bombPos.X, edy = ny - bombPos.Y;
                float edist = MathF.Sqrt(edx * edx + edy * edy);
                if (rm != null && edist <= radius && rm[nx, ny] > 0.8f) continue;
                visited[nx, ny] = true;
                queue.Enqueue((nx, ny, steps + 1));
            }
        }
        return false;
    }
}
