using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Connectors;

/// <summary>
/// In-memory runbook content connector for local / embedded runbook search.
/// Thin capability-metadata wrapper — no external I/O yet.
/// </summary>
public sealed class InMemoryRunbookConnector : IRunbookConnector
{
    private static readonly string[] ContentTypes = ["markdown", "plain-text"];

    public ConnectorDescriptor Descriptor { get; } = new(
        "in-memory-runbook",
        ConnectorKind.Runbook,
        "In-memory runbook content connector for local/embedded runbook search",
        ContentTypes);

    public IReadOnlyList<string> SupportedContentTypes => ContentTypes;

    public bool CanSearch(string contentType) =>
        SupportedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
}
