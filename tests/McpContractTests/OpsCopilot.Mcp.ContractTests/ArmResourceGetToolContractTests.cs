using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpsCopilot.Mcp.ContractTests;

/// <summary>
/// Contract tests for the "arm_resource_get" MCP tool hosted by OpsCopilot.McpHost.
///
/// These tests start McpHost as a real child process via stdio transport and
/// validate the MCP protocol contract — tool registration, input schema, and
/// input-validation guardrails — without requiring Azure credentials.
///
/// The tool fetches a single Azure resource by ARM resource ID and returns a
/// JSON envelope: { ok, resourceId, name, resourceType, location,
/// provisioningState, tagCount, error }.
///
/// Run conditions:
///   dotnet test tests/McpContractTests/OpsCopilot.Mcp.ContractTests
/// </summary>
public sealed class ArmResourceGetToolContractTests
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private const string FakeSubscriptionId = "00000000-0000-0000-0000-000000000001";
    private const string FakeResourceId =
        $"/subscriptions/{FakeSubscriptionId}/resourceGroups/rg-test/providers/Microsoft.Compute/virtualMachines/vm-ops-01";

    // ── Test: tool is registered with correct schema ──────────────────────────

    [Fact]
    public async Task ListTools_ContainsArmResourceGet_WithCorrectSchema()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);

        var tool = tools.FirstOrDefault(t => t.Name == "arm_resource_get");
        Assert.NotNull(tool);

        // description is not empty and mentions ARM
        Assert.False(string.IsNullOrWhiteSpace(tool.Description),
            "arm_resource_get description must not be empty.");
        Assert.Contains("ARM", tool.Description, StringComparison.OrdinalIgnoreCase);

        // input schema has the required resourceId property
        var schema = tool.JsonSchema;

        Assert.True(schema.TryGetProperty("properties", out var props),
            "JsonSchema must have a 'properties' object.");

        Assert.True(props.TryGetProperty("resourceId", out _),
            "JsonSchema.properties must contain 'resourceId'.");
    }

    // ── Test: empty resourceId returns ok=false ValidationError ───────────────

    [Fact]
    public async Task ArmResourceGet_EmptyResourceId_ReturnsOkFalseWithValidationError()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "arm_resource_get",
            new Dictionary<string, object?>
            {
                ["resourceId"] = "   ",   // whitespace only
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.True(root.TryGetProperty("ok", out var ok),
            "Response must have an 'ok' field.");
        Assert.False(ok.GetBoolean(), "ok must be false for an empty resourceId.");

        Assert.True(root.TryGetProperty("error", out var error),
            "Response must have an 'error' field.");
        Assert.Contains("ValidationError", error.GetString() ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    // ── Test: resourceId without /subscriptions/ prefix returns ValidationError

    [Fact]
    public async Task ArmResourceGet_MissingSubscriptionsPrefix_ReturnsOkFalseWithValidationError()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "arm_resource_get",
            new Dictionary<string, object?>
            {
                // Valid-looking ID but missing the /subscriptions/ root segment
                ["resourceId"] = "/resourceGroups/rg-test/providers/Microsoft.Compute/virtualMachines/vm-1",
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false when resourceId lacks /subscriptions/ prefix.");
        Assert.Contains("ValidationError",
            root.GetProperty("error").GetString() ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    // ── Test: well-formed resource ID without Azure creds returns ok=false ────
    //
    // The tool attempts to call Azure ARM. Without valid credentials the
    // ARM SDK raises an auth exception which the tool catches and wraps as
    // ok=false. This confirms the tool envelope contract holds even on
    // failure paths that originate from the Azure layer.

    [Fact]
    public async Task ArmResourceGet_WellFormedIdNoAzureCreds_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "arm_resource_get",
            new Dictionary<string, object?>
            {
                ["resourceId"] = FakeResourceId,
            },
            cancellationToken: cts.Token);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        var root = JsonDocument.Parse(textBlock.Text).RootElement;

        // The response envelope must always be valid JSON with ok and error fields.
        Assert.True(root.TryGetProperty("ok", out _),
            "Response must have an 'ok' field.");
        Assert.True(root.TryGetProperty("error", out _),
            "Response must have an 'error' field.");

        // Without real Azure credentials ok=false is the expected contract.
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
