namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Abstraction for runbook content retrieval and search connectors
/// (e.g. in-memory markdown store, external wiki, Confluence).
/// </summary>
public interface IRunbookConnector
{
    /// <summary>Metadata describing this connector.</summary>
    ConnectorDescriptor Descriptor { get; }

    /// <summary>Content types this connector can search (e.g. "markdown", "plain-text").</summary>
    IReadOnlyList<string> SupportedContentTypes { get; }

    /// <summary>Returns <c>true</c> when this connector supports searching the given content type.</summary>
    bool CanSearch(string contentType);
}
