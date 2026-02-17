namespace OpsCopilot.BuildingBlocks.Contracts;

public sealed class AlertPayload
{
    public string AlertId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string ResourceId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<string> Links { get; set; } = Array.Empty<string>();
    public IDictionary<string, string>? CorrelationHints { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}