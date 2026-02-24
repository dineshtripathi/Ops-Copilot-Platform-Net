namespace OpsCopilot.SafeActions.Presentation.Contracts;

/// <summary>
/// Request body for approving or rejecting an action or rollback.
/// The approver identity is extracted from the <c>x-actor-id</c> header.
/// </summary>
public sealed class ApproveActionRequest
{
    public string Reason { get; set; } = string.Empty;
}
