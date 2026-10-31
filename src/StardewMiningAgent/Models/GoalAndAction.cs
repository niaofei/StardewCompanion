using System.Collections.Immutable;

namespace StardewMiningAgent.Models;

public enum TargetResource { Coal, CopperOre, IronOre, GoldOre, IridiumOre, Skulls, Nothing }

public sealed record GoalState(
    GoalMode Mode,
    TargetResource TargetResource,
    int TargetQuantity,
    bool QuantityIsAdditional,
    int StopAtFloor,
    bool StopWhenCompleted,
    ImmutableDictionary<string, float> Constraints,
    ImmutableDictionary<string, float> Preferences,
    long Revision);

public sealed record Action(
    ActionKind Kind,
    Direction Direction,
    Vec2 Target,
    int DurationTicks,
    BombKind BombType,
    string Reason,
    float ExpectedCost);

public enum ActionKind
{
    Move, Stop, Face, Attack, SpecialAttack, Pickaxe, PlaceBomb, UseStaircase,
    EatFood, Collect, Interact, Descend, GroupEnemies, Repel, Dodge, Heal, Escape, Wait
}

public sealed record ActionPlan(
    ImmutableArray<Action> Actions,
    string Description,
    float EstimatedDuration,
    float EstimatedRisk,
    float EstimatedResourceCost,
    float ScoreBreakdownD,
    float ScoreBreakdownG,
    float ScoreBreakdownO,
    float ScoreBreakdownL,
    float ScoreBreakdownP,
    float ScoreBreakdownR,
    float ScoreBreakdownT,
    float ScoreBreakdownC);

public sealed record TaskEvaluation(
    bool CanExecute,
    float Score,
    float DamageGain,
    float GroupGain,
    float ResourceGain,
    float DepthGain,
    float PositionGain,
    float Risk,
    float TimeCost,
    float ResourceCost,
    ImmutableDictionary<string, string> Explanations);

public interface IAgentTask
{
    string Name { get; }
    TaskState State { get; }
    bool CanExecute(WorldState state, GoalState goal);
    TaskEvaluation Evaluate(WorldState state, GoalState goal);
    ActionPlan? BuildPlan(WorldState state, GoalState goal);
}
