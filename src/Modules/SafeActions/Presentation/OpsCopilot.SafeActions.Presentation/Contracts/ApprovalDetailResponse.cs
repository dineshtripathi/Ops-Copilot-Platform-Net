using OpsCopilot.SafeActions.Domain.Entities;

namespace OpsCopilot.SafeActions.Presentation.Contracts;

/// <summary>
/// Detail DTO for an individual approval record in a GET-by-id response.
/// </summary>
public sealed class ApprovalDetailResponse
{
    public Guid            ApprovalId       { get; init; }
    public string          ApproverIdentity { get; init; } = string.Empty;
    public string          Decision         { get; init; } = string.Empty;
    public string          Reason           { get; init; } = string.Empty;
    public string          Target           { get; init; } = string.Empty;
    public DateTimeOffset  CreatedAtUtc     { get; init; }

    public static ApprovalDetailResponse From(ApprovalRecord approval)
        => new()
        {
            ApprovalId       = approval.ApprovalId,
            ApproverIdentity = approval.ApproverIdentity,
            Decision         = approval.Decision.ToString(),
            Reason           = approval.Reason,
            Target           = approval.Target,
            CreatedAtUtc     = approval.CreatedAtUtc,
        };
}
