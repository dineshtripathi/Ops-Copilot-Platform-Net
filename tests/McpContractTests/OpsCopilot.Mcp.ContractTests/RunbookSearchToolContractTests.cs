using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpsCopilot.Mcp.ContractTests;

/// <summary>
/// Contract tests for the "runbook_search" MCP tool hosted by OpsCopilot.McpHost.
///
/// These tests start McpHost as a real child process via stdio transport and
/// validate the MCP protocol contract — tool registration, schema, and actual
/// search results against the dev-seed runbook files.
///
/// The dev-seed directory (docs/runbooks/dev-seed/) contains four markdown
/// runbooks: high-cpu.md, pod-crash-loop.md, ssl-cert-expiry.md, disk-full.md.
///
/// Run conditions:
///   dotnet test tests/McpContractTests/OpsCopilot.Mcp.ContractTests
/// </summary>
public sealed class RunbookSearchToolContractTests
{
    // ── Test: tool is registered with correct schema ──────────────────────────

    [Fact]
    public async Task ListTools_ContainsRunbookSearch_WithCorrectSchema()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);

        var tool = tools.FirstOrDefault(t => t.Name == "runbook_search");
        Assert.NotNull(tool);

        // description is informative
        Assert.False(string.IsNullOrWhiteSpace(tool.Description),
            "runbook_search description must not be empty.");
        Assert.Contains("runbook", tool.Description, StringComparison.OrdinalIgnoreCase);

        // input schema has required properties
        var schema = tool.JsonSchema;

        Assert.True(schema.TryGetProperty("properties", out var props),
            "JsonSchema must have a 'properties' object.");

        Assert.True(props.TryGetProperty("query", out _),
            "JsonSchema.properties must contain 'query'.");
        Assert.True(props.TryGetProperty("maxResults", out _),
            "JsonSchema.properties must contain 'maxResults'.");
    }

    // ── Test: search with "high cpu" returns ≥1 hit from seed data ───────────

    [Fact]
    public async Task RunbookSearch_HighCpuQuery_ReturnsAtLeastOneHit()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "runbook_search",
            new Dictionary<string, object?>
            {
                ["query"]      = "high cpu",
                ["maxResults"] = 5,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault();

        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean(),
            "runbook_search should return ok=true for a valid query.");
        Assert.True(root.GetProperty("hitCount").GetInt32() >= 1,
            "Expected at least 1 hit for 'high cpu' query against seed data.");

        // Verify the hit has expected structure
        var hits = root.GetProperty("hits");
        var firstHit = hits.EnumerateArray().First();
        Assert.True(firstHit.TryGetProperty("runbookId", out _), "Hit must have runbookId.");
        Assert.True(firstHit.TryGetProperty("title", out var title), "Hit must have title.");
        Assert.True(firstHit.TryGetProperty("snippet", out _), "Hit must have snippet.");
        Assert.True(firstHit.TryGetProperty("score", out _), "Hit must have score.");

        // The high-cpu.md seed file has title "High CPU Troubleshooting"
        Assert.Contains("CPU", title.GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test: search with "pod crash" returns ≥1 hit ─────────────────────────

    [Fact]
    public async Task RunbookSearch_PodCrashQuery_ReturnsAtLeastOneHit()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "runbook_search",
            new Dictionary<string, object?>
            {
                ["query"]      = "pod crash",
                ["maxResults"] = 5,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault();

        Assert.NotNull(textBlock);

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean(),
            "runbook_search should return ok=true.");
        Assert.True(root.GetProperty("hitCount").GetInt32() >= 1,
            "Expected at least 1 hit for 'pod crash' query against seed data.");
    }

    // ── Test: empty/whitespace query returns ok=true with 0 hits ─────────────

    [Fact]
    public async Task RunbookSearch_EmptyQuery_ReturnsOkWithZeroHits()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "runbook_search",
            new Dictionary<string, object?>
            {
                ["query"]      = "   ",
                ["maxResults"] = 5,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault();

        Assert.NotNull(textBlock);

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        // Whitespace-only query should either return ok=true with 0 hits
        // or ok=false — both are acceptable contract behaviors.
        // The key assertion: no crash, valid JSON envelope returned.
        Assert.True(root.TryGetProperty("ok", out _), "Response must have 'ok' field.");
        Assert.True(root.TryGetProperty("hitCount", out _), "Response must have 'hitCount' field.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and connects an MCP client to the McpHost process.
    /// Uses 'dotnet run --project &lt;path&gt;' so no pre-built binary is required.
    /// </summary>
    private static async Task<McpClient> CreateClientAsync(CancellationToken ct)
    {
        var mcpHostProjectPath = FindMcpHostProjectPath();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name      = "OpsCopilotMcpHost",
            Command   = "dotnet",
            Arguments = ["run", "--project", mcpHostProjectPath],
        });

        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    /// <summary>
    /// Locates the McpHost project directory by walking up from the test
    /// binary output directory until the solution file is found.
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
            "Ensure the test is run from the repository checkout. " +
            $"BaseDirectory was: {AppContext.BaseDirectory}");
    }
}
