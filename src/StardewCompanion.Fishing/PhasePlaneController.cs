namespace StardewCompanion.Fishing;

/// <summary>
/// Native-sign controller for Stardew's BobberBar. Normalized Y grows downwards;
/// holding use accelerates the bar upwards and releasing accelerates it downwards.
/// The simulator and the live adapter use this same sign convention.
/// </summary>
public sealed class PhasePlaneController
{
    private readonly FishingPhysics _physics;
    private readonly TackleKit _tackle;

    public PhasePlaneController(FishingPhysics physics, TackleKit tackle)
    {
        _physics = physics;
        _tackle = tackle;
    }

    public ControllerOutput Decide(FishingState state, bool treasureAvailable)
    {
        var target = state.FishPosition;
        if (treasureAvailable && state.TreasurePosition.HasValue)
        {
            var fishMargin = state.CatchProgress;
            // Prefer the chest only when catching progress has enough margin to
            // spend time away from the fish. The caller chooses whether to try it.
            if (state.TreasureProgress < 0.85f && fishMargin > 0.55f)
                target = state.TreasurePosition.Value;
        }

        var half = _physics.BarHeight / 2f;
        target = Math.Clamp(target, half, 1f - half);
        var error = target - state.BarPosition;
        var desiredVelocity = Math.Clamp(error * 0.06f, -0.012f, 0.012f);

        // Pressing adds upward acceleration (negative Y); release adds downward
        // acceleration. This velocity controller brakes before crossing target.
        return state.BarVelocity > desiredVelocity ? ControllerOutput.Press : ControllerOutput.Release;
    }

    public ControllerOutput BangBangDecide(FishingState state) => Decide(state, state.TreasureAvailable);
}
