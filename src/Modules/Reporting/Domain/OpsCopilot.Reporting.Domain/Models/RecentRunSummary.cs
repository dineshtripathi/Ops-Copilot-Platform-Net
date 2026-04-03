namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Safe, high-signal summary of a single agent-run for recent-runs display.
/// Contains only identifying and status metadata — no payload body, evidence, or secrets.
/// </summary>
public sealed record RecentRunSummary(
    Guid            RunId,
    Guid?           SessionId,
    string          Status,
    string?         AlertFingerprint,
    DateTimeOffset  CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
