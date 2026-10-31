namespace StardewCompanion.Fishing;

public sealed class FishingSimulator
{
    private readonly FishingPhysics _physics;
    private readonly FishMotion _fish;
    private readonly PhasePlaneController _controller;
    private readonly FishingGear _gear;
    private readonly int _maxTicks;
    private readonly Random _rng;
    private FishingState _state;

    public FishingState CurrentState => _state;
    public FishingGear Gear => _gear;
    public FishMotion Fish => _fish;
    public FishingPhysics Physics => _physics;

    public FishingSimulator(FishSpecies fish, FishingGear gear, int seed = 42, int maxTicks = 3600)
    {
        _maxTicks = Math.Max(0, maxTicks);
        _physics = new FishingPhysics();
        _gear = gear;
        _physics.ApplyGear(gear);
        _fish = new FishMotion(fish, seed);
        _controller = new PhasePlaneController(_physics, gear.Tackle);
        _rng = new Random(seed + 1000);

        _state = new FishingState(
            BarPosition: 0.5f,
            BarVelocity: 0f,
            FishPosition: _fish.CurrentPosition,
            CatchProgress: 0.3f,
            Tick: 0);
    }

    public FishingResult Simulate(bool spawnTreasure = true)
    {
        bool treasureSpawned = false;
        bool treasureCollected = false;
        int treasureSpawnTick = spawnTreasure ? _rng.Next(60, 180) : -1;
        float treasurePos = 0.5f;

        while (_state.Tick < _maxTicks)
        {
            _fish.Tick();
            _state = _state with { FishPosition = _fish.CurrentPosition };

            if (!treasureSpawned && spawnTreasure && _state.Tick >= treasureSpawnTick)
            {
                treasureSpawned = true;
                treasurePos = (float)_rng.NextDouble();
                _state = _state with
                {
                    TreasureActive = true,
                    TreasurePosition = treasurePos,
                    TreasureProgress = 0f
                };
            }

            if (treasureSpawned && _state.TreasureProgress >= 1f)
            {
                treasureCollected = true;
                _state = _state with
                {
                    TreasureActive = false,
                    TreasurePosition = null
                };
            }

            var output = _controller.Decide(_state, _state.TreasureAvailable);
            _physics.Tick(ref _state, output);

            if (_physics.CheckCatchSuccess(_state))
            {
                return new FishingResult(
                    Outcome: FishingOutcome.Catch,
                    FinalCatchProgress: _state.CatchProgress,
                    FinalTreasureProgress: _state.TreasureProgress,
                    TicksElapsed: _state.Tick,
                    TreasureCollected: treasureCollected);
            }

            if (_physics.CheckEscape(_state))
            {
                var outcome = treasureCollected
                    ? FishingOutcome.EscapeWithTreasureStillAvailable
                    : FishingOutcome.Escape;
                return new FishingResult(
                    Outcome: outcome,
                    FinalCatchProgress: _state.CatchProgress,
                    FinalTreasureProgress: _state.TreasureProgress,
                    TicksElapsed: _state.Tick,
                    TreasureCollected: treasureCollected);
            }
        }

        // A simulator timeout is never a catch. Real catches are confirmed only
        // by the game's FishingRod result and inventory/stat changes.
        var timeoutOutcome = FishingOutcome.TimedOut;
        return new FishingResult(
            Outcome: timeoutOutcome,
            FinalCatchProgress: _state.CatchProgress,
            FinalTreasureProgress: _state.TreasureProgress,
            TicksElapsed: _state.Tick,
            TreasureCollected: treasureCollected);
    }
}
