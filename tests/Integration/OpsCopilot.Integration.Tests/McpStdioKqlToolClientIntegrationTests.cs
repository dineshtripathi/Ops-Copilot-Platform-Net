using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Infrastructure.McpClient;
using Xunit;

namespace OpsCopilot.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="McpStdioKqlToolClient"/>.
///
/// Each test starts the real OpsCopilot.McpHost process as a child process via
/// the MCP stdio transport, exercises the input-validation path of the
/// kql_query tool, and validates the response returned by the adapter.
///
/// No Azure credentials are required — only the validation-error paths of
/// McpHost are exercised.
///
/// Run:
///   dotnet test tests/Integration/OpsCopilot.Integration.Tests
///
/// The first run may take up to 90 s because 'dotnet run' builds McpHost.
/// Pre-building McpHost with 'dotnet build' reduces this to a few seconds.
/// </summary>
public sealed class McpStdioKqlToolClientIntegrationTests : IAsyncDisposable
{
    // A single client instance is shared across all tests in this class to
    // avoid spawning the child process more than once per test session.
    private readonly McpStdioKqlToolClient _sut;

    public McpStdioKqlToolClientIntegrationTests()
    {
        var mcpHostPath  = FindMcpHostProjectPath();
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

        var options = new McpKqlServerOptions
        {
            Executable = "dotnet",
            // Pass the absolute project path so spaces are handled correctly.
            Arguments  = ["run", "--project", mcpHostPath],
            // WorkingDirectory = null → McpStdioKqlToolClient auto-discovers .sln root.
        };

        _sut = new McpStdioKqlToolClient(
            options,
            loggerFactory.CreateLogger<McpStdioKqlToolClient>());
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_InvalidWorkspaceIdGuid_ReturnsOkFalseWithValidationError()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var request = new KqlToolRequest(
            TenantId:          "tenant-integration-test",
            WorkspaceIdOrName: "THIS-IS-NOT-A-GUID",
            Kql:               "traces | take 1",
            TimespanIso8601:   "PT1H");

        var response = await _sut.ExecuteAsync(request, cts.Token);

        Assert.False(response.Ok, "Expected ok=false for an invalid workspaceId GUID.");
        Assert.NotNull(response.Error);
        Assert.Contains("ValidationError", response.Error, StringComparison.OrdinalIgnoreCase);
        // Evidence fields must always be populated for citation tracking.
        Assert.Equal(request.WorkspaceIdOrName, response.WorkspaceId);
        Assert.Equal(request.Kql,               response.ExecutedQuery);
        Assert.Equal(request.TimespanIso8601,   response.Timespan);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyKql_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var request = new KqlToolRequest(
            TenantId:          "tenant-integration-test",
            WorkspaceIdOrName: Guid.NewGuid().ToString(),
            Kql:               "   ",
            TimespanIso8601:   "PT1H");

        var response = await _sut.ExecuteAsync(request, cts.Token);

        Assert.False(response.Ok, "Expected ok=false for empty KQL.");
        Assert.NotNull(response.Error);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidTimespan_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var request = new KqlToolRequest(
            TenantId:          "tenant-integration-test",
            WorkspaceIdOrName: Guid.NewGuid().ToString(),
            Kql:               "traces | take 1",
            TimespanIso8601:   "NOT-ISO-8601");

        var response = await _sut.ExecuteAsync(request, cts.Token);

        Assert.False(response.Ok, "Expected ok=false for an invalid ISO 8601 timespan.");
        Assert.NotNull(response.Error);
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync() => await _sut.DisposeAsync();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> to the directory
    /// containing the solution file, then returns the absolute path to the
    /// OpsCopilot.McpHost project directory.
    ///
    /// Passing an absolute path to 'dotnet run --project' avoids issues with
    /// spaces in intermediate directory names.
    /// </summary>
    private static string FindMcpHostProjectPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
            {
                var path = Path.GetFullPath(
                    Path.Combine(dir.FullName, "src", "Hosts", "OpsCopilot.McpHost"));

                if (!Directory.Exists(path))
                    throw new DirectoryNotFoundException(
                        $"McpHost project not found at expected path: {path}");

                return path;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Cannot locate solution root. " +
            $"BaseDirectory was: {AppContext.BaseDirectory}");
    }
}
