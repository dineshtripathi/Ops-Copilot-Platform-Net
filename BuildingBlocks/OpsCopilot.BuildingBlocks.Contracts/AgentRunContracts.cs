namespace OpsCopilot.BuildingBlocks.Contracts;

public sealed class TriageRequest
{
    public AlertPayload Alert { get; set; } = new();
    public string? RequestedBy { get; set; }
        = "system";
}

public sealed class TriageResponse
{
    public Guid RunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public IReadOnlyList<EvidenceRef> Evidence { get; set; } = Array.Empty<EvidenceRef>();
}

public sealed class EvidenceRef
{
    public string EvidenceId { get; set; } = string.Empty;
    public string ToolCallId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}