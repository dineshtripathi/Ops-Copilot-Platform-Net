using Microsoft.Extensions.Configuration;
using Xunit;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Connectors.Infrastructure.Connectors;

namespace OpsCopilot.Modules.Connectors.Tests;

/// <summary>
/// Tests for Slice 198: GitRunbookConnector — Git-backed runbook connector.
/// </summary>
public class GitRunbookConnectorTests
{
    private const string ValidRepoUrl = "https://github.com/ops-org/runbooks.git";

    private static GitRunbookConnector Build(string? repoUrl = ValidRepoUrl)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GitRunbookConnector.RepositoryUrlConfigKey] = repoUrl
            })
            .Build();
        return new GitRunbookConnector(config);
    }

    // ── 1. Descriptor: name ──────────────────────────────────────

    [Fact]
    public void Descriptor_HasExpectedName()
    {
        var connector = Build();

        Assert.Equal("git-runbook", connector.Descriptor.Name);
    }

    // ── 2. Descriptor: kind ──────────────────────────────────────

    [Fact]
    public void Descriptor_Kind_IsRunbook()
    {
        var connector = Build();

        Assert.Equal(ConnectorKind.Runbook, connector.Descriptor.Kind);
    }

    // ── 3. CanSearch: markdown (supported) ───────────────────────

    [Fact]
    public void CanSearch_Markdown_ReturnsTrue()
    {
        var connector = Build();

        Assert.True(connector.CanSearch("markdown"));
    }

    // ── 4. CanSearch: unsupported type ───────────────────────────

    [Theory]
    [InlineData("plain-text")]
    [InlineData("json")]
    [InlineData("html")]
    [InlineData("")]
    public void CanSearch_UnsupportedType_ReturnsFalse(string contentType)
    {
        var connector = Build();

        Assert.False(connector.CanSearch(contentType));
    }

    // ── 5. CanSearch: case-insensitive ───────────────────────────

    [Theory]
    [InlineData("MARKDOWN")]
    [InlineData("Markdown")]
    [InlineData("MarkDown")]
    public void CanSearch_Markdown_IsCaseInsensitive(string contentType)
    {
        var connector = Build();

        Assert.True(connector.CanSearch(contentType));
    }

    // ── 6. SupportedContentTypes ─────────────────────────────────

    [Fact]
    public void SupportedContentTypes_ContainsMarkdownOnly()
    {
        var connector = Build();

        Assert.Single(connector.SupportedContentTypes);
        Assert.Contains("markdown", connector.SupportedContentTypes);
    }

    // ── 7. RepositoryUrl is captured from config ─────────────────

    [Fact]
    public void RepositoryUrl_IsResolvedFromConfiguration()
    {
        var connector = Build(ValidRepoUrl);

        Assert.Equal(ValidRepoUrl, connector.RepositoryUrl);
    }

    // ── 8. Missing config key throws InvalidOperationException ───

    [Fact]
    public void Constructor_MissingRepositoryUrl_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new GitRunbookConnector(config));
        Assert.Contains(GitRunbookConnector.RepositoryUrlConfigKey, ex.Message);
    }

    // ── 9. Whitespace-only URL is treated as missing ─────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhitespaceRepositoryUrl_ThrowsInvalidOperationException(string url)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GitRunbookConnector.RepositoryUrlConfigKey] = url
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new GitRunbookConnector(config));
    }

    // ── 10. Descriptor description is non-empty ──────────────────

    [Fact]
    public void Descriptor_Description_IsNonEmpty()
    {
        var connector = Build();

        Assert.False(string.IsNullOrWhiteSpace(connector.Descriptor.Description));
    }
}
