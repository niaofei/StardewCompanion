using StardewMiningAgent.Models;
using StardewMiningAgent.Combat;
using StardewMiningAgent.Mining;
using StardewMiningAgent.Navigation;
using StardewMiningAgent.Safety;

namespace StardewMiningAgent.Planning;

public sealed class UtilityEvaluator
{
    public FeatureNormalizer Normalizer { get; } = new();
    public DynamicWeightPolicy Policy { get; } = new();

    public float Evaluate(WorldState state, GoalState goal, TaskEvaluation taskEval,
        bool switchedFromPrevious = false,
        InventoryState? inv = null,
        LocalMpcPlanner? mpc = null)
    {
        float bombRatio = (inv?.Bombs ?? 1) / MathF.Max(1f, inv?.Bombs ?? 1);
        float stairRatio = (inv?.Staircases ?? 1) / MathF.Max(1f, inv?.Staircases ?? 1);
        float foodRatio = (inv?.Food ?? 1) / MathF.Max(1f, inv?.Food ?? 1);
        float energy = state.Player.Stamina / MathF.Max(1f, state.Player.MaxStamina);

        float h = state.Player.HpRatio;
        float timeBudget = 600f;
        float urgency = Math.Clamp(1f - Math.Max(0, state.Time.Hour - 6f) / Math.Max(1f, timeBudget), 0f, 1f);

        float enemyGroupability = 0f;
        if (state.Enemies.Length >= 2)
        {
            var offsets = state.Enemies.Select(e => e.Position - state.Player.Position).ToList();
            var dirConc = new GroupingPlanner().DirectionConcentration(offsets);
            enemyGroupability = dirConc;
        }

        var f = new FeatureVector(
            taskEval.DamageGain, taskEval.GroupGain, taskEval.ResourceGain, taskEval.DepthGain,
            taskEval.PositionGain, taskEval.Risk, taskEval.TimeCost, taskEval.ResourceCost);

        var ws = Policy.ComputeQ(
            BaseWeights.ForMode(goal.Mode), f, goal, state,
            switchedFromPrevious: switchedFromPrevious,
            energyShortage: energy,
            bombReserveRatio: bombRatio,
            staircaseReserveRatio: stairRatio,
            foodReserveRatio: foodRatio,
            urgency: urgency,
            enemyGroupability: enemyGroupability);

        return ws.Q;
    }

    public List<(string TaskName, float Q, float Risk, bool Allowed)> RankTasks(
        WorldState state, GoalState goal, IReadOnlyList<(string Name, TaskEvaluation Eval)> candidates,
        SafetySupervisor safety, bool switchedFromPrevious = false)
    {
        var inv = state.Inventory;
        var ranked = new List<(string TaskName, float Q, float Risk, bool Allowed)>();
        foreach (var (name, eval) in candidates)
        {
            if (!eval.CanExecute) { ranked.Add((TaskName: name, Q: float.MinValue, Risk: 0f, Allowed: false)); continue; }
            float q = Evaluate(state, goal, eval, switchedFromPrevious, inv);

            var sc = safety.CheckAction(state, null, eval.Risk, inv);
            bool allowed = sc.Approved || state.Player.HpRatio > 0.85f;
            if (!allowed) q = Math.Min(q, -10f);

            ranked.Add((TaskName: name, Q: q, Risk: eval.Risk, Allowed: allowed));
        }
        ranked.Sort((a, b) => b.Q.CompareTo(a.Q));
        return ranked;
    }

    public float PlanDiscountFactor { get; set; } = 0.9f;
    public float TerminalValueFactor { get; set; } = 0.8f;
}

public sealed class TaskSelector
{
    private readonly UtilityEvaluator _evaluator;
    private readonly float _switchPenalty;
    private readonly int _minHoldTicks;

    public TaskSelector(UtilityEvaluator evaluator, float switchPenalty = 0.08f, int minHoldTicks = 120)
    {
        _evaluator = evaluator;
        _switchPenalty = switchPenalty;
        _minHoldTicks = minHoldTicks;
    }

    public string? CurrentTask { get; private set; }
    public long CurrentTaskSinceTick { get; private set; }

    public string? Select(
        WorldState state, GoalState goal,
        IReadOnlyList<(string Name, TaskEvaluation Eval)> candidates,
        SafetySupervisor safety)
    {
        bool switched = CurrentTask != null;

        var ranked = _evaluator.RankTasks(state, goal, candidates, safety, switched);

        if (CurrentTask != null)
        {
            var heldFor = state.Tick - CurrentTaskSinceTick;
            if (heldFor < _minHoldTicks)
            {
                var current = ranked.FirstOrDefault(r => r.TaskName == CurrentTask);
                if (current.Q > float.MinValue && current.Allowed) return CurrentTask;
            }

            var bestImproved = ranked.FirstOrDefault();
            if (bestImproved.TaskName != CurrentTask && bestImproved.Q > float.MinValue)
            {
                var current = ranked.FirstOrDefault(r => r.TaskName == CurrentTask);
                float delta = bestImproved.Q - (current.Q - _switchPenalty);
                if (delta > 0.02f || !bestImproved.Allowed == false)
                {
                    CurrentTask = bestImproved.TaskName;
                    CurrentTaskSinceTick = state.Tick;
                }
            }
        }
        else
        {
            var best = ranked.FirstOrDefault(r => r.Allowed);
            if (best.TaskName != null && best.Q > -5f)
            {
                CurrentTask = best.TaskName;
                CurrentTaskSinceTick = state.Tick;
            }
        }
        return CurrentTask;
    }
}
