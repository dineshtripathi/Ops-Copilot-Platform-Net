namespace OpsCopilot.BuildingBlocks.Contracts.SafeActions;

/// <summary>
/// Contract-level exception thrown by <see cref="ISafeActionProposalService"/> when a
/// governance or policy gate denies the proposed action. Mirrors the SafeActions-internal
/// <c>PolicyDeniedException</c> without leaking module-internal types across boundaries.
/// </summary>
public sealed class SafeActionProposalDeniedException : Exception
{
    /// <summary>Frozen reason code from the policy gate (e.g. "action_type_not_allowed").</summary>
    public string ReasonCode { get; }

    public SafeActionProposalDeniedException(string reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }
}
