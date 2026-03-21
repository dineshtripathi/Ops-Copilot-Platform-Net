namespace OpsCopilot.Reporting.Domain.Models;

public sealed record HotResourceRow(
    string ResourceKey,
    string? ResourceGroup,
    int TotalRuns,
    int ExceptionSignals,
    int FailedRuns);
