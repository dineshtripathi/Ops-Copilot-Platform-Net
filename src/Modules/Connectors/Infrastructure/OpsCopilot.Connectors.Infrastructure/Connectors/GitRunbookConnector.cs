using Microsoft.Extensions.Configuration;
using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Connectors;

/// <summary>
/// Git-backed runbook connector that serves markdown runbooks from a configured
/// remote repository URL. The connector is metadata-only at this stage — the
/// repository URL is validated at startup and exposed via <see cref="RepositoryUrl"/>
/// for downstream services to use when fetching content.
/// </summary>
public sealed class GitRunbookConnector : IRunbookConnector
{
    private static readonly string[] ContentTypes = ["markdown"];

    /// <summary>Config key for the Git repository URL.</summary>
    internal const string RepositoryUrlConfigKey = "Connectors:GitRunbook:RepositoryUrl";

    public GitRunbookConnector(IConfiguration configuration)
    {
        var url = configuration[RepositoryUrlConfigKey];
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(
                $"Git runbook connector requires '{RepositoryUrlConfigKey}' in configuration.");

        RepositoryUrl = url;
    }

    /// <summary>The resolved repository URL (read-only; validated at construction).</summary>
    public string RepositoryUrl { get; }

    public ConnectorDescriptor Descriptor { get; } = new(
        "git-runbook",
        ConnectorKind.Runbook,
        "Git-backed runbook connector — serves markdown runbooks from a configured repository",
        ContentTypes);

    public IReadOnlyList<string> SupportedContentTypes => ContentTypes;

    public bool CanSearch(string contentType) =>
        SupportedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
}
