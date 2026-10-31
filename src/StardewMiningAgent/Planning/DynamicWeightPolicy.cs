using StardewMiningAgent.Models;

namespace StardewMiningAgent.Planning;

public readonly record struct BaseWeights(
    float WD, float WG, float WO, float WL, float WP, float WR, float WT, float WC)
{
    public static BaseWeights ForMonsterFarming => new(0.25f, 0.20f, 0.25f, 0.02f, 0.10f, 0.10f, 0.05f, 0.03f);
    public static BaseWeights ForSkullCavernRush => new(0.02f, 0.03f, 0.17f, 0.26f, 0.08f, 0.18f, 0.17f, 0.09f);
    public static BaseWeights ForOreFarming => new(0.04f, 0.03f, 0.40f, 0.08f, 0.10f, 0.16f, 0.12f, 0.07f);
    public static BaseWeights ForExploration => new(0.05f, 0.03f, 0.20f, 0.20f, 0.15f, 0.12f, 0.12f, 0.13f);
    public static BaseWeights ForSurvival => new(0.02f, 0.0f, 0.02f, 0.0f, 0.15f, 0.50f, 0.15f, 0.03f);

    public static BaseWeights ForMode(GoalMode mode) => mode switch
    {
        GoalMode.MonsterFarming => ForMonsterFarming,
        GoalMode.SkullCavernRush => ForSkullCavernRush,
        GoalMode.OreFarming => ForOreFarming,
        GoalMode.Exploration => ForExploration,
        GoalMode.Survival => ForSurvival,
        _ => ForMonsterFarming
    };
}

public sealed record WeightedScore(
    float WD, float WG, float WO, float WL, float WP, float WR, float WT, float WC,
    float Q, float Gain, float Penalty);

public sealed class DynamicWeightPolicy
{
    public float HpRiskCoefficient { get; set; } = 2.5f;
    public float EnclosureRiskCoefficient { get; set; } = 1.2f;
    public float LocalDangerCoefficient { get; set; } = 0.8f;
    public float EnemyGroupingCoefficient { get; set; } = 1.2f;
    public float UrgencyDepthCoefficient { get; set; } = 1.5f;
    public float BombShortageCoefficient { get; set; } = 2.0f;
    public float StaircaseShortageCoefficient { get; set; } = 1.5f;
    public float FoodShortageCoefficient { get; set; } = 1.2f;
    public float EnergyShortageCoefficient { get; set; } = 1.0f;
    public float SwitchCost { get; set; } = 0.05f;
    public bool UseDynamicAdjustment { get; set; } = true;

    public WeightedScore ComputeQ(
        BaseWeights basis,
        FeatureVector f,
        GoalState goal,
        WorldState state,
        bool switchedFromPrevious = false,
        float energyShortage = 0f,
        float bombReserveRatio = 1f,
        float staircaseReserveRatio = 1f,
        float foodReserveRatio = 1f,
        float urgency = 0f,
        float enemyGroupability = 0f)
    {
        float h = state.Player.HpRatio;
        float c = state.Enclosure;
        float z = state.DangerAtPlayer;
        float n = Math.Clamp(state.EnemyCount / 6f, 0f, 1f);

        float wd = basis.WD, wg = basis.WG, wo = basis.WO, wl = basis.WL;
        float wp = basis.WP, wr = basis.WR, wt = basis.WT, wc = basis.WC;

        if (UseDynamicAdjustment)
        {
            float healthFactor = 1f + HpRiskCoefficient * MathF.Pow(1f - h, 2)
                               + EnclosureRiskCoefficient * c
                               + LocalDangerCoefficient * z;
            wr *= healthFactor;

            wg *= (1f + EnemyGroupingCoefficient * n * enemyGroupability);

            if (goal.Mode == GoalMode.SkullCavernRush)
            {
                wl *= (1f + UrgencyDepthCoefficient * urgency);
            }

            float bombShortage = Math.Clamp(1f - bombReserveRatio, 0f, 1f);
            float stairShortage = Math.Clamp(1f - staircaseReserveRatio, 0f, 1f);
            float foodShortage = Math.Clamp(1f - foodReserveRatio, 0f, 1f);
            float newgyshortage = Math.Clamp(1f - energyShortage, 0f, 1f);

            float resourceCostMultiplier = 1f
                + BombShortageCoefficient * bombShortage * 0.4f
                + StaircaseShortageCoefficient * stairShortage * 0.3f
                + FoodShortageCoefficient * foodShortage * 0.2f
                + EnergyShortageCoefficient * newgyshortage * 0.1f;
            wc *= resourceCostMultiplier;
        }

        float sum = wd + wg + wo + wl + wp + wr + wt + wc;
        if (sum < 1e-6f) sum = 1f;
        wd /= sum; wg /= sum; wo /= sum; wl /= sum;
        wp /= sum; wr /= sum; wt /= sum; wc /= sum;

        float gain = wd * f.DamageGain + wg * f.GroupGain + wo * f.ResourceGain
                   + wl * f.DepthGain + wp * f.PositionGain;
        float penalty = wr * f.Risk + wt * f.TimeCost + wc * f.ResourceCost;
        float q = gain - penalty - (switchedFromPrevious ? SwitchCost : 0f);

        return new WeightedScore(wd, wg, wo, wl, wp, wr, wt, wc, q, gain, penalty);
    }

    public WeightedScore ComputeQStatic(BaseWeights basis, FeatureVector f, bool switched = false)
    {
        float sum = basis.WD + basis.WG + basis.WO + basis.WL + basis.WP + basis.WR + basis.WT + basis.WC;
        if (sum < 1e-6f) sum = 1f;
        float wd = basis.WD / sum, wg = basis.WG / sum, wo = basis.WO / sum;
        float wl = basis.WL / sum, wp = basis.WP / sum, wr = basis.WR / sum;
        float wt = basis.WT / sum, wc = basis.WC / sum;
        float gain = wd * f.DamageGain + wg * f.GroupGain + wo * f.ResourceGain
                   + wl * f.DepthGain + wp * f.PositionGain;
        float penalty = wr * f.Risk + wt * f.TimeCost + wc * f.ResourceCost;
        float q = gain - penalty - (switched ? SwitchCost : 0f);
        return new WeightedScore(wd, wg, wo, wl, wp, wr, wt, wc, q, gain, penalty);
    }

    public static bool ValidateNormalizedWeights(WeightedScore ws, out string? error)
    {
        error = null;
        float tol = 0.001f;
        float s = ws.WD + ws.WG + ws.WO + ws.WL + ws.WP + ws.WR + ws.WT + ws.WC;
        if (MathF.Abs(s - 1f) > tol) { error = $"权重和应为1，实际为{s}"; return false; }
        if (ws.WD < -tol || ws.WG < -tol || ws.WO < -tol || ws.WL < -tol
            || ws.WP < -tol || ws.WR < -tol || ws.WT < -tol || ws.WC < -tol)
        { error = "存在负权重"; return false; }
        if (float.IsNaN(ws.Q) || float.IsInfinity(ws.Q)) { error = "Q值为NaN或Infinity"; return false; }
        return true;
    }
}
