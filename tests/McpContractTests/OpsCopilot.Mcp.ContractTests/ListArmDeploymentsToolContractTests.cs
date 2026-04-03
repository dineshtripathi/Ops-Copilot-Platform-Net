using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpsCopilot.Mcp.ContractTests;

/// <summary>
/// Contract tests for the "list_arm_deployments" MCP tool hosted by OpsCopilot.McpHost.
///
/// These tests start McpHost as a real child process via stdio transport and
/// validate the MCP protocol contract — tool registration, input schema, and
/// input-validation guardrails — without requiring Azure credentials.
///
/// The tool lists ARM deployments for an Azure subscription and returns a
/// JSON envelope: { ok, subscriptionId, tenantId, deployments[], error }.
/// Each deployment entry has: name, timestamp, provisioningState, resourceGroup.
///
/// Run conditions:
///   dotnet test tests/McpContractTests/OpsCopilot.Mcp.ContractTests
/// </summary>
public sealed class ListArmDeploymentsToolContractTests
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private const string FakeSubscriptionId = "00000000-0000-0000-0000-000000000002";
    private const string FakeTenantId       = "00000000-0000-0000-0000-000000000099";

    // ── Test: tool is registered with correct schema ──────────────────────────

    [Fact]
    public async Task ListTools_ContainsListArmDeployments_WithCorrectSchema()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);

        var tool = tools.FirstOrDefault(t => t.Name == "list_arm_deployments");
        Assert.NotNull(tool);

        // description is not empty and mentions ARM
        Assert.False(string.IsNullOrWhiteSpace(tool.Description),
            "list_arm_deployments description must not be empty.");
        Assert.Contains("ARM", tool.Description, StringComparison.OrdinalIgnoreCase);

        // input schema has subscriptionId and tenantId parameters
        var schema = tool.JsonSchema;

        Assert.True(schema.TryGetProperty("properties", out var props),
            "JsonSchema must have a 'properties' object.");

        Assert.True(props.TryGetProperty("subscriptionId", out _),
            "JsonSchema.properties must contain 'subscriptionId'.");
        Assert.True(props.TryGetProperty("tenantId", out _),
            "JsonSchema.properties must contain 'tenantId'.");
    }

    // ── Test: non-GUID subscriptionId returns ok=false ValidationError ────────

    [Fact]
    public async Task ListArmDeployments_InvalidSubscriptionIdGuid_ReturnsOkFalseWithValidationError()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "list_arm_deployments",
            new Dictionary<string, object?>
            {
                ["subscriptionId"] = "NOT-A-VALID-GUID",
                ["tenantId"]       = FakeTenantId,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.True(root.TryGetProperty("ok", out var ok),
            "Response must have an 'ok' field.");
        Assert.False(ok.GetBoolean(), "ok must be false for an invalid subscriptionId.");

        Assert.True(root.TryGetProperty("error", out var error),
            "Response must have an 'error' field.");
        Assert.Contains("ValidationError", error.GetString() ?? "",
            StringComparison.OrdinalIgnoreCase);

        // deployments must be an empty array
        Assert.True(root.TryGetProperty("deployments", out var deployments),
            "Response must have a 'deployments' array.");
        Assert.Equal(JsonValueKind.Array, deployments.ValueKind);
        Assert.Equal(0, deployments.GetArrayLength());
    }

    // ── Test: valid GUIDs but no Azure creds → tools returns ok=false ─────────
    //
    // The tool attempts to call Azure ARM. Without valid credentials the ARM SDK
    // raises an auth exception which the tool catches and wraps as ok=false.
    // This verifies the error envelope contract holds end-to-end.

    [Fact]
    public async Task ListArmDeployments_ValidGuidsNoAzureCreds_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "list_arm_deployments",
            new Dictionary<string, object?>
            {
                ["subscriptionId"] = FakeSubscriptionId,
                ["tenantId"]       = FakeTenantId,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        // The response envelope must always be valid JSON with required fields.
        Assert.True(root.TryGetProperty("ok", out _),
            "Response must have an 'ok' field.");
        Assert.True(root.TryGetProperty("subscriptionId", out var subId),
            "Response must echo back subscriptionId.");
        Assert.Equal(FakeSubscriptionId, subId.GetString(), StringComparer.OrdinalIgnoreCase);

        Assert.True(root.TryGetProperty("deployments", out _),
            "Response must have a 'deployments' field.");

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
