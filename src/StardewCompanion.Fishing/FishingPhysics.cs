namespace StardewCompanion.Fishing;

public sealed class FishingPhysics
{
    public const float TrackHeight = 568f;
    public const float NormalizedTrackTop = 0f;
    public const float NormalizedTrackBottom = 1f;

    // Normalized Y grows downward, matching BobberBar's screen coordinates.
    // Releasing falls; holding the native use button accelerates upward.
    public float GravityPerTick { get; } = 0.003f;
    public float ThrustPerTick { get; } = 0.0035f;
    public float MaxSpeed { get; } = 0.5f;
    public float MinSpeed { get; } = -0.5f;

    public float BarHeight { get; private set; }
    public float BounceRestitution { get; private set; } = 2f / 3f;
    public float FillPerTick { get; private set; } = 0.005f;
    public float DrainPerTick { get; private set; } = 0.0075f;

    public bool TreasureHunterActive { get; private set; }

    public void ApplyGear(FishingGear gear)
    {
        float level = gear.FishingLevel;
        if (gear.Tackle.TrainingRod) level = Math.Max(level, 5);

        float barPixels = 96f + 8f * level
                        + 24f * gear.Tackle.CorkBobbers
                        + (gear.Tackle.DeluxeBait ? 12f : 0f);
        BarHeight = barPixels / TrackHeight;

        float leadDamping = Math.Min(gear.Tackle.LeadBobbers * 0.1f, 1f);
        BounceRestitution = (2f / 3f) * (1f - leadDamping);

        float drainMult = 1f;
        if (gear.Tackle.TrapBobbers == 1) drainMult = 0.67f;
        else if (gear.Tackle.TrapBobbers >= 2) drainMult = 0.5f;
        if (gear.Tackle.TrainingRod) drainMult = 0.67f;
        DrainPerTick = 0.0075f * drainMult;

        FillPerTick = 0.005f;
        TreasureHunterActive = gear.Tackle.TreasureHunters > 0;
    }

    public void SetNativeBarHeight(float pixels) => BarHeight = Math.Clamp(pixels / TrackHeight, 0.05f, 0.95f);

    public void Tick(ref FishingState state, ControllerOutput input)
    {
        float acceleration = input == ControllerOutput.Press
            ? GravityPerTick - ThrustPerTick
            : GravityPerTick;

        state = state with
        {
            BarVelocity = Math.Clamp(state.BarVelocity + acceleration, MinSpeed, MaxSpeed)
        };

        float min = BarHeight / 2f;
        float max = NormalizedTrackBottom - min;
        float newPos = state.BarPosition + state.BarVelocity;

        if (newPos >= max)
        {
            newPos = max;
            float bounced = -state.BarVelocity * BounceRestitution;
            state = state with { BarVelocity = bounced };
        }
        else if (newPos <= min)
        {
            newPos = min;
            state = state with { BarVelocity = Math.Max(0f, -state.BarVelocity * BounceRestitution) };
        }

        state = state with { BarPosition = newPos };

        float barTop = state.BarPosition - BarHeight / 2f;
        float barBottom = state.BarPosition + BarHeight / 2f;
        bool fishInBar = state.FishPosition >= barTop && state.FishPosition <= barBottom;

        bool fishIsTarget = true;
        if (state.TreasureAvailable)
        {
            float treBarTop = state.TreasurePosition!.Value - BarHeight / 2f;
            float treBarBottom = state.TreasurePosition.Value + BarHeight / 2f;
            fishIsTarget = fishInBar;
        }

        if (fishInBar)
        {
            float newProgress = Math.Min(state.CatchProgress + FillPerTick, 1f);
            state = state with { CatchProgress = newProgress };
        }
        else
        {
            bool skipDrain = TreasureHunterActive && state.TreasureAvailable &&
                             IsBarOverTreasure(state);
            if (!skipDrain)
            {
                float newProgress = Math.Max(state.CatchProgress - DrainPerTick, 0f);
                state = state with { CatchProgress = newProgress };
            }
        }

        if (state.TreasureAvailable)
        {
            float treBarTop = state.TreasurePosition!.Value - BarHeight / 2f;
            float treBarBottom = state.TreasurePosition.Value + BarHeight / 2f;
            bool treInBar = state.BarPosition >= treBarTop && state.BarPosition <= treBarBottom;
            if (treInBar)
            {
                state = state with
                {
                    TreasureProgress = Math.Min(state.TreasureProgress + FillPerTick, 1f)
                };
            }
        }

        state = state with { Tick = state.Tick + 1 };
    }

    private bool IsBarOverTreasure(FishingState s)
    {
        if (!s.TreasurePosition.HasValue) return false;
        float treTop = s.TreasurePosition.Value - BarHeight / 2f;
        float treBot = s.TreasurePosition.Value + BarHeight / 2f;
        return s.BarPosition >= treTop && s.BarPosition <= treBot;
    }

    public bool CheckCatchSuccess(FishingState state) => state.CatchProgress >= 1f;
    public bool CheckEscape(FishingState state) => state.CatchProgress <= 0f;
    public bool CheckTreasureCollected(FishingState state) => state.TreasureProgress >= 1f;
}
