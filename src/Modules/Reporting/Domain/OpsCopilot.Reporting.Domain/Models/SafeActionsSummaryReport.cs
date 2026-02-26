namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Aggregate status counts for safe-action records within an optional date range.
/// </summary>
public sealed record SafeActionsSummaryReport(
    int TotalActions,
    int Proposed,
    int Approved,
    int Rejected,
    int Executing,
    int Completed,
    int Failed,
    DateTime? FromUtc,
    DateTime? ToUtc);
