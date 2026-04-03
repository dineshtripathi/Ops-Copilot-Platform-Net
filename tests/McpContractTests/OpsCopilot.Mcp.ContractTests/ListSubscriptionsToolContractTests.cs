using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpsCopilot.Mcp.ContractTests;

/// <summary>
/// Contract tests for the "list_subscriptions" MCP tool hosted by OpsCopilot.McpHost.
///
/// These tests start McpHost as a real child process via stdio transport and
/// validate the MCP protocol contract — tool registration, input schema, and
/// input-validation guardrails — without requiring Azure credentials.
///
/// The tool lists Azure subscriptions accessible to the managed identity filtered
/// by tenant ID, returning a JSON envelope:
/// { ok, tenantId, subscriptionCount, accessible, active, subscriptions[], error }.
/// Each subscription entry has: subscriptionId, displayName, state.
///
/// This tool is the MCP boundary for Reporting.Infrastructure's
/// ITenantEstateProvider — part of the OpsCopilot tenant estate discovery
/// that feeds the ops dashboard with subscription visibility.
///
/// Run conditions:
///   dotnet test tests/McpContractTests/OpsCopilot.Mcp.ContractTests
/// </summary>
public sealed class ListSubscriptionsToolContractTests
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private const string FakeTenantId = "00000000-0000-0000-0000-000000000099";

    // ── Test: tool is registered with correct schema ──────────────────────────

    [Fact]
    public async Task ListTools_ContainsListSubscriptions_WithCorrectSchema()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);

        var tool = tools.FirstOrDefault(t => t.Name == "list_subscriptions");
        Assert.NotNull(tool);

        // description is not empty
        Assert.False(string.IsNullOrWhiteSpace(tool.Description),
            "list_subscriptions description must not be empty.");
        Assert.Contains("subscription", tool.Description, StringComparison.OrdinalIgnoreCase);

        // input schema has the tenantId parameter
        var schema = tool.JsonSchema;

        Assert.True(schema.TryGetProperty("properties", out var props),
            "JsonSchema must have a 'properties' object.");

        Assert.True(props.TryGetProperty("tenantId", out _),
            "JsonSchema.properties must contain 'tenantId'.");
    }

    // ── Test: non-GUID tenantId returns ok=false ValidationError ─────────────

    [Fact]
    public async Task ListSubscriptions_InvalidTenantIdGuid_ReturnsOkFalseWithValidationError()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "list_subscriptions",
            new Dictionary<string, object?>
            {
                ["tenantId"] = "THIS-IS-NOT-A-GUID",
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.True(root.TryGetProperty("ok", out var ok),
            "Response must have an 'ok' field.");
        Assert.False(ok.GetBoolean(), "ok must be false for an invalid tenantId.");

        Assert.True(root.TryGetProperty("error", out var error),
            "Response must have an 'error' field.");
        Assert.Contains("ValidationError", error.GetString() ?? "",
            StringComparison.OrdinalIgnoreCase);

        // subscriptions must be an empty array
        Assert.True(root.TryGetProperty("subscriptions", out var subscriptions),
            "Response must have a 'subscriptions' array.");
        Assert.Equal(JsonValueKind.Array, subscriptions.ValueKind);
        Assert.Equal(0, subscriptions.GetArrayLength());
    }

    // ── Test: valid GUID tenantId but no Azure creds → ok=false ──────────────
    //
    // The tool attempts to call Azure ARM GetSubscriptionsAsync. Without valid
    // credentials the ARM SDK raises an auth exception which the tool catches
    // and wraps as ok=false. This verifies the envelope contract end-to-end
    // including the numeric fields (subscriptionCount, accessible, active).

    [Fact]
    public async Task ListSubscriptions_ValidTenantIdNoAzureCreds_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "list_subscriptions",
            new Dictionary<string, object?>
            {
                ["tenantId"] = FakeTenantId,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        // The response envelope must always be valid JSON with the full field set.
        Assert.True(root.TryGetProperty("ok", out _),
            "Response must have an 'ok' field.");
        Assert.True(root.TryGetProperty("tenantId", out var tenantIdEcho),
            "Response must echo back tenantId.");
        Assert.Equal(FakeTenantId, tenantIdEcho.GetString(), StringComparer.OrdinalIgnoreCase);

        Assert.True(root.TryGetProperty("subscriptionCount", out _),
            "Response must have 'subscriptionCount' field.");
        Assert.True(root.TryGetProperty("accessible", out _),
            "Response must have 'accessible' field.");
        Assert.True(root.TryGetProperty("active", out _),
            "Response must have 'active' field.");
        Assert.True(root.TryGetProperty("subscriptions", out _),
            "Response must have 'subscriptions' array.");

        // Without real Azure credentials, ok=false is the expected contract.
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
