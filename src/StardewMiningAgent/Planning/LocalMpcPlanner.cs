using StardewMiningAgent.Models;
using StardewMiningAgent.Combat;
using StardewMiningAgent.Navigation;

namespace StardewMiningAgent.Planning;

public sealed class LocalMpcPlanner
{
    public int PlanningHorizonTicks { get; set; } = 20;
    public int ResolveIntervalTicks { get; set; } = 4;
    public float DiscountFactor { get; set; } = 0.95f;
    public bool EnableEnemyPredictiveCollisions { get; set; } = true;

    private readonly DynamicWeightPolicy _weights;
    private readonly RiskMap? _riskMap;
    private readonly WeaponModel _weapon;

    public LocalMpcPlanner(WeaponModel weapon, RiskMap? riskMap = null, DynamicWeightPolicy? weights = null)
    {
        _weapon = weapon;
        _riskMap = riskMap;
        _weights = weights ?? new DynamicWeightPolicy();
    }

    public MpcAction Decide(WorldState state, GoalState goal, FeatureNormalizer normalizer)
    {
        var actions = GenerateCandidateActions(state);
        float bestQ = float.MinValue;
        MpcAction best = MpcAction.Wait;

        foreach (var candidate in actions)
        {
            float totalQ = SimulateRolling(state, goal, candidate, normalizer);
            if (totalQ > bestQ) { bestQ = totalQ; best = candidate; }
        }
        return best with { QValue = bestQ };
    }

    private List<MpcAction> GenerateCandidateActions(WorldState state)
    {
        var list = new List<MpcAction>();
        var m = state.Map;
        var p = state.Player;

        Span<(Direction d, Vec2 offset)> dirs = stackalloc (Direction, Vec2)[8]
        {
            (Direction.Up, new Vec2(0, -1)),
            (Direction.Down, new Vec2(0, 1)),
            (Direction.Left, new Vec2(-1, 0)),
            (Direction.Right, new Vec2(1, 0)),
            (Direction.UpRight, new Vec2(0.707f, -0.707f)),
            (Direction.UpLeft, new Vec2(-0.707f, -0.707f)),
            (Direction.DownRight, new Vec2(0.707f, 0.707f)),
            (Direction.DownLeft, new Vec2(-0.707f, 0.707f)),
        };

        foreach (var (d, off) in dirs)
        {
            Vec2 next = p.Position + off;
            int tx = (int)MathF.Round(next.X), ty = (int)MathF.Round(next.Y);
            if (m.InBounds(tx, ty) && m.IsWalkable(tx, ty))
            {
                list.Add(new MpcAction(MpcActionKind.Move, d));
            }
        }

        foreach (var e in state.Enemies)
        {
            var vdir = (e.Position - p.Position).Normalized();
            var facings = new[]
            {
                (Direction.Right, new Vec2(1, 0)), (Direction.Left, new Vec2(-1, 0)),
                (Direction.Up, new Vec2(0, -1)), (Direction.Down, new Vec2(0, 1)),
                (Direction.UpRight, new Vec2(0.707f, -0.707f)),
                (Direction.UpLeft, new Vec2(-0.707f, -0.707f)),
                (Direction.DownRight, new Vec2(0.707f, 0.707f)),
                (Direction.DownLeft, new Vec2(-0.707f, 0.707f)),
            };
            foreach (var (dir, v) in facings)
            {
                float dot = v.X * vdir.X + v.Y * vdir.Y;
                if (dot >= 0.3f) list.Add(new MpcAction(MpcActionKind.Attack, dir, e.Id));
            }
        }

        list.Add(MpcAction.Wait);
        return list;
    }

    private float SimulateRolling(WorldState state, GoalState goal, MpcAction action, FeatureNormalizer n)
    {
        float total = 0f;
        float gamma = 1f;
        int steps = PlanningHorizonTicks / ResolveIntervalTicks;
        var origin = state.Player.Position;

        for (int k = 0; k < steps; k++)
        {
            var predicted = PredictNextStep(state, action, (k + 1) * ResolveIntervalTicks, origin);
            var f = predicted.FeatureVector;
            var ws = _weights.ComputeQ(
                BaseWeights.ForMode(goal.Mode), f, goal, state,
                switchedFromPrevious: false,
                enemyGroupability: predicted.Groupability);
            total += gamma * ws.Q;
            gamma *= DiscountFactor;
        }
        return total;
    }

    private (Vec2 PlayerPos, FeatureVector FeatureVector, float Groupability) PredictNextStep(
        WorldState state, MpcAction action, int offsetTicks, Vec2 startPos)
    {
        var newPos = startPos;
        float timeSec = offsetTicks / 60f;
        int enemiesHit = 0;
        float damageExp = 0f;
        float groupExp = 0f;
        float riskAtPos = 0f;

        if (action.Kind == MpcActionKind.Move)
        {
            var fv = FacingVec(action.Direction);
            // Predict from the observed origin and elapsed horizon time once;
            // cumulative time must not be added to an already advanced position.
            newPos = state.Player.Position + fv * state.Player.Speed * timeSec;
        }

        foreach (var e in state.Enemies)
        {
            var ePos = EnableEnemyPredictiveCollisions
                ? new Vec2(e.Position.X + e.VelX * timeSec, e.Position.Y + e.VelY * timeSec)
                : e.Position;

            if (action.Kind == MpcActionKind.Attack && _weapon.CanHit(newPos, action.Direction, ePos))
            {
                enemiesHit++;
                damageExp += _weapon.EstimateDamage();
            }
            float d = ePos.DistanceTo(newPos);
            float sigma = Math.Max(1.5f, e.AttackRange);
            riskAtPos += MathF.Exp(-(d * d) / (2 * sigma * sigma));
        }

        float groupability = enemiesHit >= 2 ? 1f : enemiesHit >= 1 ? 0.3f : 0f;
        float O = 0f, L = 0f, P = 0f;
        float T = 0.5f + (action.Kind == MpcActionKind.Wait ? 1f : 0f);
        float C = action.Kind == MpcActionKind.PlaceBomb ? 1f : 0f;
        float R = MathF.Min(1f, riskAtPos);

        var f = new FeatureNormalizer().Normalize(damageExp, groupExp, O, L, P, R, T, C);
        return (newPos, f, groupability);
    }

    private static Vec2 FacingVec(Direction d) => d switch
    {
        Direction.Up => new Vec2(0, -1), Direction.Down => new Vec2(0, 1),
        Direction.Left => new Vec2(-1, 0), Direction.Right => new Vec2(1, 0),
        Direction.UpRight => new Vec2(0.707f, -0.707f), Direction.UpLeft => new Vec2(-0.707f, -0.707f),
        Direction.DownRight => new Vec2(0.707f, 0.707f), Direction.DownLeft => new Vec2(-0.707f, 0.707f),
        _ => Vec2.Zero
    };
}

public enum MpcActionKind { Move, Attack, PlaceBomb, UseStaircase, Heal, Escape, Wait }

public sealed record MpcAction(
    MpcActionKind Kind,
    Direction Direction = Direction.Down,
    int TargetEnemyId = -1,
    float QValue = float.MinValue)
{
    public static MpcAction Wait => new(MpcActionKind.Wait);
}
