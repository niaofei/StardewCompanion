using StardewMiningAgent.Models;

namespace StardewMiningAgent.Planning;

public sealed record FeatureVector(
    float DamageGain,
    float GroupGain,
    float ResourceGain,
    float DepthGain,
    float PositionGain,
    float Risk,
    float TimeCost,
    float ResourceCost);

public sealed class FeatureNormalizer
{
    public float DamageRef { get; set; } = 30f;
    public float GroupRef { get; set; } = 1f;
    public float ResourceRef { get; set; } = 50f;
    public float DepthRef { get; set; } = 1f;
    public float PositionRef { get; set; } = 1f;
    public float RiskRef { get; set; } = 40f;
    public float TimeRef { get; set; } = 1800f;
    public float CostRef { get; set; } = 3f;

    public FeatureVector Normalize(
        float damageExp, float groupExp, float resourceExp, float depthExp,
        float positionExp, float riskExp, float timeExp, float costExp)
    {
        float N(float x, float @ref) => @ref <= 0 ? 0f : Math.Clamp(x / @ref, 0f, 1f);
        return new FeatureVector(
            DamageGain: N(damageExp, DamageRef),
            GroupGain: N(groupExp, GroupRef),
            ResourceGain: N(resourceExp, ResourceRef),
            DepthGain: N(depthExp, DepthRef),
            PositionGain: N(positionExp, PositionRef),
            Risk: N(riskExp, RiskRef),
            TimeCost: N(timeExp, TimeRef),
            ResourceCost: N(costExp, CostRef));
    }

    public static float Clamp01(float x) => Math.Clamp(x, 0f, 1f);
}
