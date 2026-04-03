using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpsCopilot.Mcp.ContractTests;

/// <summary>
/// Contract tests for the "cost_query" MCP tool hosted by OpsCopilot.McpHost.
///
/// These tests start McpHost as a real child process via stdio transport and
/// validate the MCP protocol contract — tool registration, input schema, and
/// input-validation guardrails — without requiring Azure credentials.
///
/// The tool queries Azure Cost Management for actual pre-tax costs grouped by
/// service name and returns a JSON envelope:
/// { ok, subscriptionId, resourceGroupName, scope, from, to, currency,
///   totalCost, rowCount, rows, error }.
///
/// Run conditions:
///   dotnet test tests/McpContractTests/OpsCopilot.Mcp.ContractTests
/// </summary>
public sealed class CostQueryToolContractTests
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private const string FakeSubscriptionId = "00000000-0000-0000-0000-000000000001";

    // ── Test: tool is registered with correct schema ──────────────────────────

    [Fact]
    public async Task ListTools_ContainsCostQuery_WithCorrectSchema()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);

        var tool = tools.FirstOrDefault(t => t.Name == "cost_query");
        Assert.NotNull(tool);

        Assert.False(string.IsNullOrWhiteSpace(tool.Description),
            "cost_query description must not be empty.");
        Assert.Contains("cost", tool.Description, StringComparison.OrdinalIgnoreCase);

        // Input schema must expose the required subscriptionId property.
        var schema = tool.JsonSchema;

        Assert.True(schema.TryGetProperty("properties", out var props),
            "JsonSchema must have a 'properties' object.");

        Assert.True(props.TryGetProperty("subscriptionId", out _),
            "JsonSchema.properties must contain 'subscriptionId'.");

        Assert.True(props.TryGetProperty("lookbackDays", out _),
            "JsonSchema.properties must contain 'lookbackDays'.");
    }

    // ── Test: invalid GUID for subscriptionId returns ok=false ───────────────

    [Fact]
    public async Task CostQuery_InvalidSubscriptionIdGuid_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "cost_query",
            new Dictionary<string, object?>
            {
                ["subscriptionId"] = "not-a-guid",
                ["lookbackDays"]   = 30,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.True(root.TryGetProperty("ok", out var ok),
            "Response must have an 'ok' field.");
        Assert.False(ok.GetBoolean(), "ok must be false for an invalid GUID.");

        Assert.True(root.TryGetProperty("error", out var error),
            "Response must have an 'error' field.");
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()),
            "error must not be empty.");
    }

    // ── Test: lookbackDays = 0 returns ok=false ───────────────────────────────

    [Fact]
    public async Task CostQuery_LookbackDaysZero_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "cost_query",
            new Dictionary<string, object?>
            {
                ["subscriptionId"] = FakeSubscriptionId,
                ["lookbackDays"]   = 0,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false when lookbackDays is 0.");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()),
            "error must not be empty.");
    }

    // ── Test: lookbackDays = 91 returns ok=false ──────────────────────────────

    [Fact]
    public async Task CostQuery_LookbackDays91_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "cost_query",
            new Dictionary<string, object?>
            {
                ["subscriptionId"] = FakeSubscriptionId,
                ["lookbackDays"]   = 91,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false when lookbackDays exceeds 90.");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()),
            "error must not be empty.");
    }

    // ── Test: valid GUID without Azure creds returns ok=false ─────────────────
    //
    // The tool attempts to call Azure Cost Management. Without valid credentials
    // the SDK raises an auth exception which the tool wraps as ok=false.
    // Confirms the JSON envelope contract holds even on Azure failure paths.

    [Fact]
    public async Task CostQuery_ValidGuidNoAzureCreds_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "cost_query",
            new Dictionary<string, object?>
            {
                ["subscriptionId"] = FakeSubscriptionId,
                ["lookbackDays"]   = 7,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.True(root.TryGetProperty("ok", out _),
            "Response must have an 'ok' field.");
        Assert.True(root.TryGetProperty("error", out _),
            "Response must have an 'error' field.");

        // Without real Azure credentials ok=false is the expected contract.
        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false when no Azure credentials are available.");
    }

    // ── Test: valid GUID with optional resourceGroupName uses RG scope ────────

    [Fact]
    public async Task CostQuery_WithResourceGroupName_NoAzureCreds_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "cost_query",
            new Dictionary<string, object?>
            {
                ["subscriptionId"]    = FakeSubscriptionId,
                ["resourceGroupName"] = "rg-test",
                ["lookbackDays"]      = 14,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.True(root.TryGetProperty("ok", out _),
            "Response must have an 'ok' field.");
        Assert.True(root.TryGetProperty("error", out _),
            "Response must have an 'error' field.");

        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false when no Azure credentials are available.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<McpClient> CreateClientAsync(CancellationToken ct)
    {
        var mcpHostProjectPath = FindMcpHostProjectPath();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name      = "OpsCopilotMcpHost",
            Command   = "dotnet",
            Arguments = ["run", "--project", mcpHostProjectPath, "--no-build", "--configuration", Configuration],
        });

        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

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
