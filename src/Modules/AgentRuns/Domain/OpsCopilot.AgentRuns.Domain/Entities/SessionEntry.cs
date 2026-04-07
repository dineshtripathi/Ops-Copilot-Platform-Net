namespace OpsCopilot.AgentRuns.Domain.Entities;

/// <summary>
/// Persisted session record for the SQL-backed session store.
/// Sessions are created via <see cref="Create"/> and are never mutated except
/// to extend the expiry via <see cref="Touch"/>.
/// </summary>
public sealed class SessionEntry
{
    // EF Core private constructor
    private SessionEntry() { }

    public static SessionEntry Create(string tenantId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
        => new()
        {
            SessionId    = Guid.NewGuid(),
            TenantId     = tenantId,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc,
        };

    public Guid           SessionId    { get; private set; }
    public string         TenantId     { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>Extends the session expiry. No-op if already past the new time.</summary>
    public void Touch(DateTimeOffset newExpiresAt) => ExpiresAtUtc = newExpiresAt;
}
