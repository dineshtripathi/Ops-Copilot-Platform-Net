namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Thrown when a caller supplies a session ID that belongs to a different tenant.
/// The presentation layer maps this to HTTP 403 Forbidden.
/// </summary>
public sealed class SessionTenantMismatchException : InvalidOperationException
{
    public Guid    SessionId     { get; }
    public string  OwnerTenantId { get; }
    public string  CallerTenantId { get; }

    public SessionTenantMismatchException(Guid sessionId, string ownerTenantId, string callerTenantId)
        : base($"Session {sessionId} belongs to tenant '{ownerTenantId}' but caller presented tenant '{callerTenantId}'.")
    {
        SessionId      = sessionId;
        OwnerTenantId  = ownerTenantId;
        CallerTenantId = callerTenantId;
    }
}
