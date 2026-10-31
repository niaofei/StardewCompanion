using StardewMiningAgent.Models;

namespace StardewMiningAgent.Combat;

public sealed class WeaponModel
{
    public WeaponKind Kind { get; init; } = WeaponKind.Sword;
    public float Range { get; init; } = 1.5f;
    public float KnockbackBase { get; init; } = 0.5f;
    public float BaseDamage { get; init; } = 18f;
    public float SwingTime { get; init; } = 0.4f;
    public float HitWindowBegin { get; init; } = 0.1f;
    public float HitWindowEnd { get; init; } = 0.35f;

    public bool CanHit(Vec2 playerPos, Direction facing, Vec2 enemyPos, float enemyHitRadius = 0.4f)
    {
        float dist = playerPos.DistanceTo(enemyPos);
        if (dist > Range + enemyHitRadius) return false;

        var fdir = FacingToVec(facing);
        var edir = (enemyPos - playerPos);
        float elen = edir.Length;
        if (elen < 0.01f) return true;
        edir = new Vec2(edir.X / elen, edir.Y / elen);

        float dot = fdir.X * edir.X + fdir.Y * edir.Y;
        float cosHalfAngle = Kind switch
        {
            WeaponKind.Sword => 0.55f,
            WeaponKind.Dagger => 0.4f,
            WeaponKind.Club => 0.65f,
            _ => 0.5f
        };
        return dot >= cosHalfAngle;
    }

    public float EstimateDamage(float playerLevelBonus = 0f) => BaseDamage + playerLevelBonus;

    public float EstimateKnockback() => KnockbackBase;

    public float SwingDurationTicks => SwingTime * 60f;

    public (bool inWindow, float progress) HitWindowProgress(float animTime)
    {
        float p = animTime / SwingTime;
        return (p >= HitWindowBegin / SwingTime && p <= HitWindowEnd / SwingTime, p);
    }

    private static Vec2 FacingToVec(Direction d) => d switch
    {
        Direction.Up => new Vec2(0, -1), Direction.Down => new Vec2(0, 1),
        Direction.Left => new Vec2(-1, 0), Direction.Right => new Vec2(1, 0),
        Direction.UpRight => new Vec2(0.707f, -0.707f), Direction.UpLeft => new Vec2(-0.707f, -0.707f),
        Direction.DownRight => new Vec2(0.707f, 0.707f), Direction.DownLeft => new Vec2(-0.707f, 0.707f),
        _ => Vec2.Zero
    };

    public static WeaponModel Create(WeaponKind kind) => kind switch
    {
        WeaponKind.Sword => new WeaponModel { Kind = WeaponKind.Sword, Range = 1.8f, BaseDamage = 25f, KnockbackBase = 0.6f, SwingTime = 0.45f },
        WeaponKind.Dagger => new WeaponModel { Kind = WeaponKind.Dagger, Range = 1.2f, BaseDamage = 14f, KnockbackBase = 0.3f, SwingTime = 0.28f },
        WeaponKind.Club => new WeaponModel { Kind = WeaponKind.Club, Range = 1.6f, BaseDamage = 32f, KnockbackBase = 1.0f, SwingTime = 0.55f },
        _ => new WeaponModel { Kind = kind }
    };
}

public sealed class EnemyPredictor
{
    private const int HistoryWindow = 6;

    public Vec2 PredictPosition(EnemyState e, float secondsAhead)
    {
        Vec2 basePred = new Vec2(e.Position.X + e.VelX * secondsAhead, e.Position.Y + e.VelY * secondsAhead);
        if (e.PositionHistory.Length < 3) return basePred;

        var h = e.PositionHistory;
        int n = h.Length;
        float recentVx = (h[^1].X - h[Math.Max(0, n - 3)].X) / 0.05f;
        float recentVy = (h[^1].Y - h[Math.Max(0, n - 3)].Y) / 0.05f;
        float smoothWeight = Math.Clamp(secondsAhead / 0.5f, 0f, 1f);
        float alpha = smoothWeight;
        var vx = e.VelX * (1 - alpha) + recentVx * alpha;
        var vy = e.VelY * (1 - alpha) + recentVy * alpha;
        return new Vec2(e.Position.X + vx * secondsAhead, e.Position.Y + vy * secondsAhead);
    }

    public float PredictVelocity(EnemyState e)
    {
        return MathF.Sqrt(e.VelX * e.VelX + e.VelY * e.VelY);
    }

    public float ProjectileImpactTime(EnemyState e, Vec2 playerPos)
    {
        if (!e.HasProjectile) return float.PositiveInfinity;
        float d = e.Position.DistanceTo(playerPos);
        float speed = 4f;
        return d / speed;
    }

    public float AttackWindowEstimate(EnemyState e, Vec2 playerPos)
    {
        float d = e.Position.DistanceTo(playerPos);
        if (d > e.AttackRange + 0.5f) return float.MaxValue;
        return e.Behavior switch
        {
            EnemyBehavior.Hopping => 0.35f,
            EnemyBehavior.Fly => 0.25f,
            EnemyBehavior.Charging => 0.15f,
            EnemyBehavior.Projectile => Math.Max(0.4f, ProjectileImpactTime(e, playerPos)),
            _ => 0.3f
        };
    }

    public float PredictConfidence(EnemyState e) => e.PredictionConfidence;
}

public sealed class GroupingPlanner
{
    public float DirectionConcentration(IReadOnlyList<Vec2> enemyOffsetsFromPlayer)
    {
        int n = enemyOffsetsFromPlayer.Count;
        if (n <= 1) return 0f;
        float sx = 0f, sy = 0f;
        foreach (var o in enemyOffsetsFromPlayer)
        {
            var l = o.Length;
            if (l < 0.1f) continue;
            sx += o.X / l;
            sy += o.Y / l;
        }
        return MathF.Min(1f, MathF.Sqrt(sx * sx + sy * sy) / MathF.Max(1, n));
    }

    public float Compactness(IReadOnlyList<Vec2> positions, Vec2 center)
    {
        int n = positions.Count;
        if (n <= 1) return 0f;
        float sum = 0f;
        foreach (var p in positions) sum += p.DistanceTo(center);
        return sum / n;
    }

    public float EvaluateGrouping(
        Vec2 playerPos,
        List<Vec2> enemyPositions,
        Vec2 candidateGroupPoint,
        float weaponRange,
        out int estimatedGroupHitCount,
        out float estimatedRisk,
        out float estimatedTime)
    {
        estimatedGroupHitCount = 0;
        estimatedRisk = 0f;
        estimatedTime = 0f;
        if (enemyPositions.Count == 0) return 0f;

        var offsets = enemyPositions.Select(p => p - candidateGroupPoint).ToList();
        float dirConc = DirectionConcentration(offsets);
        float compactness = Compactness(enemyPositions, candidateGroupPoint);

        estimatedGroupHitCount = 0;
        foreach (var p in enemyPositions)
            if (p.DistanceTo(candidateGroupPoint) <= weaponRange) estimatedGroupHitCount++;

        float travelDist = playerPos.DistanceTo(candidateGroupPoint);
        estimatedTime = travelDist / 4f + 0.5f;

        foreach (var p in enemyPositions)
        {
            float d = p.DistanceTo(candidateGroupPoint);
            estimatedRisk += MathF.Exp(-d * d / (2 * 2f));
        }

        float g = 1.2f * dirConc + 2.0f * estimatedGroupHitCount
                - 0.6f * estimatedRisk - 0.3f * estimatedTime - 0.8f * compactness;
        return g;
    }

    public Vec2? FindBestGroupingPoint(
        Vec2 playerPos, List<Vec2> enemyPositions, float weaponRange, MapState map)
    {
        if (enemyPositions.Count == 0) return null;

        float cx = enemyPositions.Average(p => p.X);
        float cy = enemyPositions.Average(p => p.Y);

        float bestScore = float.MinValue;
        Vec2 bestPoint = playerPos;

        for (float r = 0.5f; r <= weaponRange + 0.5f; r += 0.5f)
        {
            for (float a = 0; a < MathF.PI * 2f; a += MathF.PI / 6f)
            {
                Vec2 candidate = new(cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r);
                int tx = (int)MathF.Round(candidate.X), ty = (int)MathF.Round(candidate.Y);
                if (!map.InBounds(tx, ty) || !map.IsWalkable(tx, ty)) continue;
                if (candidate.DistanceTo(playerPos) > 8f) continue;

                var g = EvaluateGrouping(playerPos, enemyPositions, candidate, weaponRange,
                    out int nHit, out float risk, out float time);
                if (g > bestScore) { bestScore = g; bestPoint = candidate; }
            }
        }
        return bestScore > -10f ? bestPoint : null;
    }
}

public sealed class KnockbackPlanner
{
    public float EstimateReturnTime(Vec2 enemyPos, Vec2 playerPos, float knockback, float enemyApproachSpeed)
    {
        float distAfter = enemyPos.DistanceTo(playerPos) + knockback;
        float dDanger = 1.5f;
        if (distAfter <= dDanger) return 0f;
        return Math.Max(0, (distAfter - dDanger) / Math.Max(0.5f, enemyApproachSpeed));
    }

    public float SafetyTimeGained(WorldState state, WeaponModel weapon)
    {
        float total = 0f;
        float kb = weapon.EstimateKnockback();
        foreach (var e in state.Enemies)
        {
            float speed = MathF.Sqrt(e.VelX * e.VelX + e.VelY * e.VelY);
            if (speed < 0.1f) speed = 1.5f;
            total += EstimateReturnTime(e.Position, state.Player.Position, kb, speed);
        }
        return total;
    }
}
