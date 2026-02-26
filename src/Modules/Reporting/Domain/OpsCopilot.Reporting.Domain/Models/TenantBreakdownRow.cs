namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// A single row in the tenant breakdown report.
/// </summary>
public sealed record TenantBreakdownRow(
    string TenantId,
    int TotalActions,
    int Completed,
    int Failed);
