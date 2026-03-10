namespace OpsCopilot.AgentRuns.Application.Orchestration;

internal static class ModelCostEstimator
{
    private static readonly Dictionary<string, (decimal InputPerM, decimal OutputPerM)> Rates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4o"]      = (2.50m,  10.00m),
            ["gpt-4o-mini"] = (0.15m,   0.60m),
            ["gpt-4-turbo"] = (10.00m, 30.00m),
            ["default"]     = (0.00m,   0.00m),
        };

    public static decimal Estimate(string? modelId, int inputTokens, int outputTokens)
    {
        var (inRate, outRate) = modelId is not null && Rates.TryGetValue(modelId, out var r) ? r : (0m, 0m);
        return (inputTokens * inRate + outputTokens * outRate) / 1_000_000m;
    }
}
