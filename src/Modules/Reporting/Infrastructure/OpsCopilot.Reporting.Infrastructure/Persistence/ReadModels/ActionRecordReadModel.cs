namespace OpsCopilot.Reporting.Infrastructure.Persistence.ReadModels;

/// <summary>
/// Lightweight read-only projection of the safeActions.ActionRecords table.
/// Used exclusively for reporting queries — no mutations, no navigation properties.
/// </summary>
internal sealed class ActionRecordReadModel
{
    public Guid ActionRecordId { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public Guid RunId { get; init; }
    public string ActionType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RollbackStatus { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ExecutedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
}
