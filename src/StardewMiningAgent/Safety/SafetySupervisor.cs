using StardewMiningAgent.Models;

namespace StardewMiningAgent.Safety;

public sealed class StuckDetector
{
    public int ImmobileTicksThreshold { get; set; } = 10;
    public float PositionDriftThreshold { get; set; } = 0.3f;
    public int GoalRepeatThreshold { get; set; } = 4;
    public int AttackNoEffectThreshold { get; set; } = 30;

    private readonly Queue<Vec2> _recentPositions = new();
    private readonly Queue<long> _tickLog = new();
    private readonly Queue<string> _recentGoalNames = new();
    private readonly Queue<float> _enemyHpHistory = new();

    public void RecordPosition(long tick, Vec2 pos)
    {
        _recentPositions.Enqueue(pos);
        _tickLog.Enqueue(tick);
        while (_recentPositions.Count > 20) { _recentPositions.Dequeue(); _tickLog.Dequeue(); }
    }

    public void RecordGoalChange(string goalName)
    {
        _recentGoalNames.Enqueue(goalName);
        while (_recentGoalNames.Count > GoalRepeatThreshold * 2) _recentGoalNames.Dequeue();
    }

    public void RecordAttackTick(float enemyHpSum)
    {
        _enemyHpHistory.Enqueue(enemyHpSum);
        while (_enemyHpHistory.Count > AttackNoEffectThreshold) _enemyHpHistory.Dequeue();
    }

    public StuckStatus Detect(WorldState state)
    {
        var issues = new List<string>();

        if (_recentPositions.Count >= 8)
        {
            var first = _recentPositions.Peek();
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in _recentPositions)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            float spanX = maxX - minX, spanY = maxY - minY;
            if (spanX < PositionDriftThreshold && spanY < PositionDriftThreshold)
                issues.Add($"连续 {_recentPositions.Count} 帧未移动");
        }

        if (_recentGoalNames.Count >= GoalRepeatThreshold * 2)
        {
            var seq = _recentGoalNames.ToArray();
            bool oscillation = true;
            for (int i = 2; i < seq.Length; i++)
            {
                if (seq[i] != seq[i - 2]) { oscillation = false; break; }
            }
            if (oscillation) issues.Add($"在多个目标之间反复切换: {string.Join(",", seq)}");
        }

        if (_enemyHpHistory.Count >= AttackNoEffectThreshold)
        {
            var first = _enemyHpHistory.Peek();
            var last = _enemyHpHistory.ToArray()[^1];
            if (MathF.Abs(first - last) < 0.5f && state.EnemyCount > 0)
                issues.Add("持续攻击但未造成有效伤害");
        }

        return new StuckStatus(issues.Count > 0, issues);
    }
}

public sealed record StuckStatus(bool IsStuck, List<string> Reasons);

public sealed class SafetySupervisor
{
    public float MinimumHealthRatio { get; set; } = 0.35f;
    public float CriticalHealthRatio { get; set; } = 0.15f;
    public float MaxAcceptableRisk { get; set; } = 0.6f;
    public float UnacceptableDeathRisk { get; set; } = 0.9f;
    public float LowHealthFoodTriggerRatio { get; set; } = 0.5f;

    public SafetyCheck CheckAction(WorldState state, ActionPlan? plan, float estimatedAdditionalRisk, InventoryState inv)
    {
        var issues = new List<string>();

        float h = state.Player.HpRatio;

        if (h < CriticalHealthRatio)
        {
            issues.Add($"血量极低 ({h * 100:F1}%)，必须执行紧急恢复");
            return new SafetyCheck(false, RiskLevel.Critical, issues, true);
        }

        float projectedRisk = state.DangerAtPlayer + estimatedAdditionalRisk;
        if (projectedRisk > UnacceptableDeathRisk && h < 0.8f)
        {
            issues.Add($"预计风险 {projectedRisk * 100:F0}% 过高，当前血量不支持此操作");
        }

        if (h < MinimumHealthRatio && inv.Food == 0)
        {
            issues.Add("血量低于安全阈值且无食物");
        }

        if (plan != null && plan.Actions.Length > 0)
        {
            bool hasBomb = plan.Actions.Any(a => a.Kind == ActionKind.PlaceBomb);
            if (hasBomb && inv.Bombs == 0) issues.Add("计划使用炸弹但库存不足");
            bool hasStair = plan.Actions.Any(a => a.Kind == ActionKind.UseStaircase);
            if (hasStair && inv.Staircases == 0) issues.Add("计划使用楼梯道具但库存不足");
        }

        bool ok = issues.Count == 0 || h >= 0.8f;
        var level = h < 0.3f ? RiskLevel.Critical
                  : h < 0.5f ? RiskLevel.High
                  : h < 0.7f ? RiskLevel.Medium
                  : RiskLevel.Safe;

        return new SafetyCheck(ok, level, issues, h < CriticalHealthRatio);
    }

    public bool ShouldEatFood(WorldState state)
    {
        return state.Player.HpRatio < LowHealthFoodTriggerRatio;
    }

    public bool ShouldFlee(WorldState state)
    {
        if (state.Player.HpRatio < CriticalHealthRatio) return true;
        if (state.Enemies.Length >= 4 && state.Enclosure > 0.5f && state.Player.HpRatio < MinimumHealthRatio) return true;
        return false;
    }

    public bool FoodAvailable(InventoryState inv) => inv.Food > 0;
}

public sealed record SafetyCheck(bool Approved, RiskLevel Level, List<string> Concerns, bool Emergency);
