namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>
/// Typed representation of an inbound alert payload for POST /agent/triage.
///
/// Required fields  : <see cref="AlertSource"/>, <see cref="Fingerprint"/>
/// Optional fields  : all remaining properties
/// Extensibility    : <see cref="Dimensions"/> for structured key-value labels;
///                    <see cref="Raw"/> for any additional fields the source system emits.
///
/// Serialization note: this DTO is serialized back to compact JSON at the
/// presentation boundary to compute the ledger fingerprint and to pass a stable
/// string to downstream services that currently expect raw JSON. This is a
/// temporary compatibility bridge; the application layer will accept the typed
/// record directly in a future slice.
/// </summary>
public sealed record AlertPayloadDto(
    /// <summary>
    /// Identifier for the monitoring system or rule that raised the alert,
    /// e.g. "AzureMonitor", "Prometheus", "Datadog".
    /// Required; must not be empty or whitespace.
    /// </summary>
    string AlertSource,

    /// <summary>
    /// Fingerprint embedded in the alert by the source system, used to
    /// correlate repeated firings of the same underlying condition.
    /// Required; must not be empty or whitespace.
    /// </summary>
    string Fingerprint,

    /// <summary>Human-readable alert title, e.g. "High CPU on web-01".</summary>
    string? Title = null,

    /// <summary>Severity label, e.g. "Critical", "Warning", "Informational".</summary>
    string? Severity = null,

    /// <summary>UTC timestamp when the alert was fired by the source system.</summary>
    DateTimeOffset? FiredAtUtc = null,

    /// <summary>Signal type, e.g. "Metric", "Log", "ActivityLog".</summary>
    string? SignalType = null,

    /// <summary>
    /// Azure resource ID of the affected resource, e.g.
    /// "/subscriptions/.../resourceGroups/.../providers/.../virtualMachines/vm-01".
    /// </summary>
    string? ResourceId = null,

    /// <summary>Logical service or application name, e.g. "payment-service".</summary>
    string? ServiceName = null,

    /// <summary>Deployment environment, e.g. "production", "staging".</summary>
    string? Environment = null,

    /// <summary>
    /// Correlation ID that links this alert to a trace, incident, or change-set.
    /// </summary>
    string? CorrelationId = null,

    /// <summary>
    /// Structured dimension labels from the alert, e.g.
    /// { "region": "uksouth", "tier": "web" }.
    /// </summary>
    Dictionary<string, string>? Dimensions = null,

    /// <summary>
    /// Pass-through bag for any additional fields emitted by the source system
    /// that do not map to the typed properties above. Preserved in the ledger.
    /// </summary>
    Dictionary<string, object?>? Raw = null);
