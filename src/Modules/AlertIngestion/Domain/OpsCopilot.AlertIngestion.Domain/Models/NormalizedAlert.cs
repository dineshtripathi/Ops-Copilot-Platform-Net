namespace OpsCopilot.AlertIngestion.Domain.Models;

/// <summary>
/// Canonical normalized alert model produced by provider-specific normalizers.
/// Every ingested alert — regardless of source — is mapped to this shape
/// before fingerprinting, validation, and persistence.
/// </summary>
public sealed record NormalizedAlert
{
    /// <summary>Provider key that produced the alert (e.g. "azure_monitor", "datadog").</summary>
    public required string Provider { get; init; }

    /// <summary>Provider-assigned unique identifier for the alert instance.</summary>
    public required string AlertExternalId { get; init; }

    /// <summary>Short human-readable title / rule name.</summary>
    public required string Title { get; init; }

    /// <summary>Longer description (may be null for some providers).</summary>
    public string? Description { get; init; }

    /// <summary>Normalised severity: Critical, Error, Warning, Informational.</summary>
    public required string Severity { get; init; }

    /// <summary>UTC timestamp when the alert fired at the source.</summary>
    public required DateTime FiredAtUtc { get; init; }

    /// <summary>Fully-qualified resource identifier (ARM id, Datadog host, etc.).</summary>
    public required string ResourceId { get; init; }

    /// <summary>Source type / signal kind (e.g. "Metric", "Log", "Event").</summary>
    public required string SourceType { get; init; }

    /// <summary>Arbitrary key-value dimensions extracted from the payload.</summary>
    public IReadOnlyDictionary<string, string>? Dimensions { get; init; }

    /// <summary>Original raw JSON payload preserved verbatim for audit.</summary>
    public required string RawPayload { get; init; }

    /// <summary>
    /// Deterministic SHA-256 fingerprint computed from canonical fields
    /// (Provider + Title + ResourceId + Severity + SourceType).
    /// Populated after normalisation by the fingerprint service.
    /// </summary>
    public string Fingerprint { get; init; } = string.Empty;
}
