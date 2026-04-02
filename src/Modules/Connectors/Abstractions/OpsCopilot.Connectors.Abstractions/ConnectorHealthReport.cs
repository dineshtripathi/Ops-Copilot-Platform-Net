namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Result of a connector credential health check.
/// </summary>
/// <param name="ConnectorName">The connector that was checked.</param>
/// <param name="IsHealthy"><c>true</c> when the credential is present and non-empty.</param>
/// <param name="CheckedAt">UTC timestamp when the check was performed.</param>
/// <param name="FailureReason">Human-readable reason when <see cref="IsHealthy"/> is <c>false</c>.</param>
public sealed record ConnectorHealthReport(
    string          ConnectorName,
    bool            IsHealthy,
    DateTimeOffset  CheckedAt,
    string?         FailureReason = null);
