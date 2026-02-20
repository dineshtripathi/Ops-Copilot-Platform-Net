using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpsCopilot.Mcp.ContractTests;

/// <summary>
/// Contract tests for the OpsCopilot.McpHost MCP tool server.
///
/// These tests start McpHost as a real child process via stdio transport and
/// validate the MCP protocol contract — tool registration and input validation —
/// without requiring Azure credentials.
///
/// Run conditions:
///   dotnet test tests/McpContractTests/OpsCopilot.Mcp.ContractTests
///
/// The tests use a long timeout (~60 s) because 'dotnet run' performs a build
/// on first invocation. For faster iteration, pre-build McpHost and the test
/// will complete in a few seconds.
/// </summary>
public sealed class KqlToolContractTests
{
    // ── Test: tool is registered ───────────────────────────────────────────────

    [Fact]
    public async Task ListTools_ContainsKqlQuery_WithCorrectSchema()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        // ── list tools ──────────────────────────────────────────────────────
        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);

        var kqlTool = tools.FirstOrDefault(t => t.Name == "kql_query");
        Assert.NotNull(kqlTool);

        // ── description is informative ──────────────────────────────────────
        Assert.False(string.IsNullOrWhiteSpace(kqlTool.Description),
            "kql_query description must not be empty.");
        Assert.Contains("KQL", kqlTool.Description, StringComparison.OrdinalIgnoreCase);

        // ── input schema has required properties ────────────────────────────
        // McpClientTool (AIFunction) exposes JsonSchema, not InputSchema
        var schema = kqlTool.JsonSchema;

        Assert.True(schema.TryGetProperty("properties", out var props),
            "JsonSchema must have a 'properties' object.");

        Assert.True(props.TryGetProperty("workspaceId", out _),
            "JsonSchema.properties must contain 'workspaceId'.");
        Assert.True(props.TryGetProperty("kql", out _),
            "JsonSchema.properties must contain 'kql'.");
        Assert.True(props.TryGetProperty("timespan", out _),
            "JsonSchema.properties must contain 'timespan'.");

        // ── required array names all three parameters ───────────────────────
        if (schema.TryGetProperty("required", out var required))
        {
            var requiredNames = required.EnumerateArray()
                .Select(e => e.GetString())
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains("workspaceId", requiredNames);
            Assert.Contains("kql", requiredNames);
            Assert.Contains("timespan", requiredNames);
        }
        // Note: absence of "required" is not a contract failure for this SDK version.
    }

    // ── Test: validation — bad GUID returns ok=false (no Azure creds needed) ──

    [Fact]
    public async Task KqlQuery_InvalidWorkspaceIdGuid_ReturnsOkFalseWithValidationError()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "kql_query",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "THIS-IS-NOT-A-GUID",
                ["kql"]         = "traces | take 5",
                ["timespan"]    = "PT1H",
            },
            cancellationToken: cts.Token);

        // The tool returns a JSON string as text content.
        var textBlock = result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault();

        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var response = JsonDocument.Parse(textBlock.Text);
        var root     = response.RootElement;

        Assert.True(root.TryGetProperty("ok", out var ok),
            "Response must have an 'ok' field.");
        Assert.False(ok.GetBoolean(), "ok must be false for an invalid workspaceId.");

        Assert.True(root.TryGetProperty("error", out var error),
            "Response must have an 'error' field.");
        var errorText = error.GetString() ?? "";
        Assert.Contains("ValidationError", errorText, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test: validation — bad ISO 8601 duration returns ok=false ─────────────

    [Fact]
    public async Task KqlQuery_InvalidTimespan_ReturnsOkFalseWithValidationError()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "kql_query",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = Guid.NewGuid().ToString(),
                ["kql"]         = "traces | take 5",
                ["timespan"]    = "NOT-ISO-8601",
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);

        var root = JsonDocument.Parse(textBlock.Text).RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());

        var errorText = root.GetProperty("error").GetString() ?? "";
        Assert.Contains("ValidationError", errorText, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test: validation — empty kql returns ok=false ─────────────────────────

    [Fact]
    public async Task KqlQuery_EmptyKql_ReturnsOkFalseWithValidationError()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "kql_query",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = Guid.NewGuid().ToString(),
                ["kql"]         = "   ",   // whitespace only
                ["timespan"]    = "PT1H",
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);

        var root = JsonDocument.Parse(textBlock.Text).RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
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
