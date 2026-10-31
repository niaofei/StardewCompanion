using System.Collections.Immutable;

namespace StardewMiningAgent.Models;

public readonly record struct Vec2(float X, float Y)
{
    public static Vec2 Zero => new(0f, 0f);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 v, float s) => new(v.X * s, v.Y * s);
    public float Length => MathF.Sqrt(X * X + Y * Y);
    public Vec2 Normalized() { var l = Length; return l < 1e-6f ? Zero : new Vec2(X / l, Y / l); }
    public float DistanceTo(Vec2 o) => (this - o).Length;
    public float TileDistance(Vec2 o) { var d = this - o; return MathF.Max(MathF.Abs(d.X), MathF.Abs(d.Y)); }
    public int ToTileX => (int)MathF.Round(X);
    public int ToTileY => (int)MathF.Round(Y);
    public static Vec2 FromTile(int tx, int ty) => new(tx + 0.5f, ty + 0.5f);
}

public sealed record PlayerState(
    Vec2 Position,
    int TileX,
    int TileY,
    Direction Facing,
    int Hp,
    int MaxHp,
    int Stamina,
    int MaxStamina,
    float Speed,
    WeaponKind Weapon,
    float AttackRange,
    bool CanMove,
    bool IsAttacking,
    bool IsInvulnerable)
{
    public float HpRatio => MaxHp > 0 ? (float)Hp / MaxHp : 0f;
    public Vec2 FacingVec => Facing switch
    {
        Direction.Up => new Vec2(0, -1),
        Direction.Down => new Vec2(0, 1),
        Direction.Left => new Vec2(-1, 0),
        Direction.Right => new Vec2(1, 0),
        Direction.UpRight => new Vec2(0.707f, -0.707f),
        Direction.UpLeft => new Vec2(-0.707f, -0.707f),
        Direction.DownRight => new Vec2(0.707f, 0.707f),
        Direction.DownLeft => new Vec2(-0.707f, 0.707f),
        _ => Vec2.Zero
    };
}

public sealed record EnemyState(
    int Id,
    string Kind,
    Vec2 Position,
    float VelX,
    float VelY,
    int Hp,
    int MaxHp,
    EnemyBehavior Behavior,
    bool HasProjectile,
    float AttackRange,
    float KnockbackFactor,
    ImmutableArray<Vec2> PositionHistory,
    float PredictionConfidence)
{
    public float HpRatio => MaxHp > 0 ? (float)Hp / MaxHp : 0f;
    public Vec2 Velocity => new(VelX, VelY);
}

public sealed record MapState(
    MineKind MineKind,
    int Floor,
    int Width,
    int Height,
    ImmutableArray<ImmutableArray<TileKind>> Tiles,
    ImmutableArray<Vec2> LadderPositions,
    ImmutableArray<Vec2> MineEntrances,
    ImmutableArray<Vec2> PickupPositions,
    ImmutableHashSet<(int, int)> BombTiles,
    ImmutableHashSet<(int, int)> ExploredTiles)
{
    public bool InBounds(int tx, int ty) => tx >= 0 && tx < Width && ty >= 0 && ty < Height;

    public bool IsWalkable(int tx, int ty)
    {
        if (!InBounds(tx, ty)) return false;
        var t = Tiles[ty][tx];
        return t is TileKind.Empty or TileKind.LadderDown or TileKind.MineEntrance;
    }

    public bool IsDestructible(int tx, int ty)
    {
        if (!InBounds(tx, ty)) return false;
        var t = Tiles[ty][tx];
        return t is TileKind.Stone or TileKind.Copper or TileKind.Iron or TileKind.Gold
               or TileKind.Iridium or TileKind.Special or TileKind.Crate;
    }
}

public sealed record InventoryState(
    int Coal,
    int CopperOre,
    int IronOre,
    int GoldOre,
    int IridiumOre,
    int Bombs,
    int Staircases,
    int Food,
    int SpentSlots,
    int TotalSlots)
{
    public bool CanAfford(BombKind kind) => kind switch
    {
        BombKind.Small => Bombs >= 1,
        BombKind.Big => Bombs >= 1,
        BombKind.Dynamite => Bombs >= 1,
        _ => false
    };
}

public enum BombKind { Small, Big, Dynamite }

public sealed record WorldState(
    GameTime Time,
    PlayerState Player,
    ImmutableArray<EnemyState> Enemies,
    MapState Map,
    InventoryState Inventory,
    long Tick,
    Vec2 LastPlayerPosition,
    float PlayerImmobileTicks,
    ImmutableArray<int> RecentHurtTicks)
{
    public int EnemyCount => Enemies.Length;

    public float DangerAtPlayer
    {
        get
        {
            float risk = 0f;
            foreach (var e in Enemies)
            {
                var d = e.Position.DistanceTo(Player.Position);
                var sigma = Math.Max(0.5f, e.AttackRange);
                risk += MathF.Exp(-(d * d) / (2f * sigma * sigma));
            }
            return MathF.Min(1f, risk * 0.8f);
        }
    }

    public float SurroundingConcentration
    {
        get
        {
            var n = Enemies.Length;
            if (n <= 1) return 0f;
            float sx = 0, sy = 0;
            var pp = Player.Position;
            foreach (var e in Enemies)
            {
                var dx = e.Position.X - pp.X;
                var dy = e.Position.Y - pp.Y;
                var l = MathF.Sqrt(dx * dx + dy * dy);
                if (l < 0.1f) continue;
                sx += dx / l;
                sy += dy / l;
            }
            var mag = MathF.Sqrt(sx * sx + sy * sy);
            return MathF.Min(1f, mag / MathF.Max(1, n - 1));
        }
    }

    public float Enclosure
    {
        get
        {
            var ptx = Player.TileX;
            var pty = Player.TileY;
            int blocked = 0;
            int total = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                total++;
                if (Map.InBounds(ptx + dx, pty + dy) && !Map.IsWalkable(ptx + dx, pty + dy)) blocked++;
            }
            return total > 0 ? (float)blocked / total : 0f;
        }
    }
}

public readonly record struct GameTime(int Day, int Hour, int Minute, int Second);
