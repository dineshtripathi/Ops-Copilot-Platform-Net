namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// A single row in the action-type breakdown report.
/// </summary>
public sealed record ActionTypeBreakdownRow(
    string ActionType,
    int Count,
    int Completed,
    int Failed);
