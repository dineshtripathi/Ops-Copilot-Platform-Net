namespace OpsCopilot.SafeActions.Application.Orchestration;

/// <summary>
/// Thrown when a governance policy denies a proposed safe action.
/// The endpoint layer catches this to return 400 Bad Request with the
/// structured <see cref="ReasonCode"/> and <see cref="Message"/>.
/// </summary>
public sealed class PolicyDeniedException : Exception
{
    public string ReasonCode { get; }

    public PolicyDeniedException(string reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }
}
