namespace StardewCompanion.Fishing;

public sealed class FishMotion
{
    private readonly Random _rng;
    private readonly FishBehavior _behavior;
    private readonly int _difficulty;
    private float _velocity;
    private float _targetPos;
    private int _timeSinceLastJump;
    private int _jumpInterval;

    public float CurrentPosition { get; private set; }
    public float CurrentVelocity => _velocity;

    public FishMotion(FishSpecies species, int seed = 42)
    {
        _behavior = species.Behavior;
        _difficulty = species.Difficulty;
        _rng = new Random(seed);
        CurrentPosition = 0.5f;
        _velocity = 0f;
        _targetPos = 0.5f;
        _timeSinceLastJump = 0;
        _jumpInterval = ComputeJumpInterval();
    }

    private int ComputeJumpInterval()
    {
        float baseInterval = _behavior switch
        {
            FishBehavior.Dart => 30f,
            FishBehavior.Mixed => 60f,
            FishBehavior.Smooth => 120f,
            FishBehavior.Floater => 90f,
            FishBehavior.Sinker => 90f,
            _ => 80f
        };
        float diffFactor = 15f / Math.Max(_difficulty, 1);
        return (int)(baseInterval * diffFactor * (0.5f + _rng.NextDouble()));
    }

    public void Tick()
    {
        float baseSpeed = 0.002f + _difficulty * 0.00003f;

        switch (_behavior)
        {
            case FishBehavior.Smooth:
                MoveTowardTarget(baseSpeed * 0.7f);
                break;

            case FishBehavior.Mixed:
                MoveTowardTarget(baseSpeed);
                break;

            case FishBehavior.Dart:
                MoveTowardTarget(baseSpeed * 1.1f);
                break;

            case FishBehavior.Floater:
                CurrentPosition = Math.Min(1f, CurrentPosition - baseSpeed * 0.3f);
                MoveTowardTarget(baseSpeed * 0.5f);
                break;

            case FishBehavior.Sinker:
                CurrentPosition = Math.Max(0f, CurrentPosition + baseSpeed * 0.3f);
                MoveTowardTarget(baseSpeed * 0.5f);
                break;
        }

        _timeSinceLastJump++;
        if (_timeSinceLastJump >= _jumpInterval)
        {
            _timeSinceLastJump = 0;
            _jumpInterval = ComputeJumpInterval();
            TriggerJump();
        }

        CurrentPosition = Math.Clamp(CurrentPosition, 0f, 1f);
        _velocity = _targetPos - CurrentPosition;
    }

    private void MoveTowardTarget(float speed)
    {
        float error = _targetPos - CurrentPosition;
        CurrentPosition += Math.Sign(error) * Math.Min(Math.Abs(error), speed);
        if (Math.Abs(error) < 0.01f)
        {
            _targetPos = (float)_rng.NextDouble();
        }
    }

    private void TriggerJump()
    {
        float dartAmplitude = _difficulty switch
        {
            < 30 => 0.05f,
            < 60 => 0.12f,
            < 90 => 0.20f,
            _ => 0.28f
        };
        float jump = (_rng.NextSingle() - 0.5f) * 2f * dartAmplitude;
        CurrentPosition = Math.Clamp(CurrentPosition + jump, 0f, 1f);

        if (_behavior == FishBehavior.Dart && _rng.NextDouble() < 0.5)
        {
            CurrentPosition = (float)_rng.NextDouble();
        }
        else if (_behavior == FishBehavior.Mixed && _rng.NextDouble() < 0.3)
        {
            _targetPos = (float)_rng.NextDouble();
        }

        CurrentPosition = Math.Clamp(CurrentPosition, 0f, 1f);
    }

    public void Reset(float position = 0.5f)
    {
        CurrentPosition = position;
        _velocity = 0f;
        _targetPos = position;
        _timeSinceLastJump = 0;
        _jumpInterval = ComputeJumpInterval();
    }
}
