namespace StardewCompanion.Fishing;

public enum FishBehavior
{
    Smooth,
    Mixed,
    Dart,
    Floater,
    Sinker
}

public enum TackleType
{
    None,
    CorkBobber,
    LeadBobber,
    TrapBobber,
    TreasureHunter,
    BarbedHook,
    QualityBobber,
    Magnet
}

public sealed record TackleKit(
    int CorkBobbers = 0,
    int LeadBobbers = 0,
    int TrapBobbers = 0,
    int TreasureHunters = 0,
    int BarbedHooks = 0,
    bool DeluxeBait = false,
    bool TrainingRod = false);

public sealed record FishSpecies(
    string Name,
    int Difficulty,
    FishBehavior Behavior);

public sealed record FishingGear(
    int FishingLevel,
    TackleKit Tackle);

public sealed record FishingState(
    float BarPosition,
    float BarVelocity,
    float FishPosition,
    float CatchProgress,
    float? TreasurePosition = null,
    float TreasureProgress = 0f,
    bool TreasureActive = false,
    int Tick = 0)
{
    public bool TreasureAvailable => TreasureActive && TreasurePosition.HasValue;
}

public enum ControllerOutput
{
    Release,
    Press
}

public enum FishingOutcome
{
    Catch,
    Escape,
    TimedOut,
    TreasureCollected,
    EscapeWithTreasureStillAvailable
}

public sealed record FishingResult(
    FishingOutcome Outcome,
    float FinalCatchProgress,
    float FinalTreasureProgress,
    int TicksElapsed,
    bool TreasureCollected);
