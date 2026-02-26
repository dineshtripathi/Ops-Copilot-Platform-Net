namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// A single row in the recent-actions report.
/// </summary>
public sealed record RecentActionRow(
    Guid ActionRecordId,
    string TenantId,
    string ActionType,
    string Status,
    string RollbackStatus,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);
