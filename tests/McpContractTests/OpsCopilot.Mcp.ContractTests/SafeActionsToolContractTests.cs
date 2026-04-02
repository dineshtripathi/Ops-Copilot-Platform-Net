using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpsCopilot.Mcp.ContractTests;

/// <summary>
/// Contract tests for the "safe_action_list" and "safe_action_propose" MCP tools
/// hosted by OpsCopilot.McpHost.
///
/// These tests start McpHost as a real child process via stdio transport and validate:
/// - Tool registration and JSON schema (required + optional parameters).
/// - Input-validation guardrails (empty tenantId, invalid GUIDs, limit bounds, bad JSON).
/// - Graceful failure when <c>ApiHost:BaseUrl</c> is not configured.
///
/// Tests do NOT require a live ApiHost — all paths terminate in ok=false with a
/// descriptive error field, which is the expected contract for calls made without
/// a configured ApiHost.
///
/// Run conditions:
///   dotnet test tests/McpContractTests/OpsCopilot.Mcp.ContractTests
/// </summary>
public sealed class SafeActionsToolContractTests
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private static readonly string FakeRunId    = "10000000-0000-0000-0000-000000000001";
    private const           string FakeTenantId = "tenant-a";

    // ── Schema: safe_action_list ──────────────────────────────────────────────

    [Fact]
    public async Task ListTools_ContainsSafeActionList_WithCorrectSchema()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);

        var tool = tools.FirstOrDefault(t => t.Name == "safe_action_list");
        Assert.NotNull(tool);

        Assert.False(string.IsNullOrWhiteSpace(tool.Description),
            "safe_action_list description must not be empty.");
        Assert.Contains("safe action", tool.Description, StringComparison.OrdinalIgnoreCase);

        var schema = tool.JsonSchema;
        Assert.True(schema.TryGetProperty("properties", out var props),
            "JsonSchema must have a 'properties' object.");

        Assert.True(props.TryGetProperty("tenantId", out _),
            "JsonSchema.properties must contain 'tenantId'.");

        Assert.True(props.TryGetProperty("runId", out _),
            "JsonSchema.properties must contain 'runId'.");

        Assert.True(props.TryGetProperty("limit", out _),
            "JsonSchema.properties must contain 'limit'.");
    }

    // ── Schema: safe_action_propose ───────────────────────────────────────────

    [Fact]
    public async Task ListTools_ContainsSafeActionPropose_WithCorrectSchema()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);

        var tool = tools.FirstOrDefault(t => t.Name == "safe_action_propose");
        Assert.NotNull(tool);

        Assert.False(string.IsNullOrWhiteSpace(tool.Description),
            "safe_action_propose description must not be empty.");
        Assert.Contains("safe action", tool.Description, StringComparison.OrdinalIgnoreCase);

        var schema = tool.JsonSchema;
        Assert.True(schema.TryGetProperty("properties", out var props),
            "JsonSchema must have a 'properties' object.");

        Assert.True(props.TryGetProperty("tenantId", out _),
            "properties must contain 'tenantId'.");

        Assert.True(props.TryGetProperty("runId", out _),
            "properties must contain 'runId'.");

        Assert.True(props.TryGetProperty("actionType", out _),
            "properties must contain 'actionType'.");

        Assert.True(props.TryGetProperty("proposedPayloadJson", out _),
            "properties must contain 'proposedPayloadJson'.");
    }

    // ── Validation: safe_action_list ──────────────────────────────────────────

    [Fact]
    public async Task SafeActionList_EmptyTenantId_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "safe_action_list",
            new Dictionary<string, object?> { ["tenantId"] = "" },
            cancellationToken: cts.Token);

        var root = ParseRoot(result);
        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false for an empty tenantId.");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()),
            "error must not be empty.");
    }

    [Fact]
    public async Task SafeActionList_InvalidRunIdGuid_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "safe_action_list",
            new Dictionary<string, object?>
            {
                ["tenantId"] = FakeTenantId,
                ["runId"]    = "not-a-guid",
            },
            cancellationToken: cts.Token);

        var root = ParseRoot(result);
        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false for an invalid runId GUID.");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()),
            "error must not be empty.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task SafeActionList_LimitOutOfRange_ReturnsOkFalse(int badLimit)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "safe_action_list",
            new Dictionary<string, object?>
            {
                ["tenantId"] = FakeTenantId,
                ["limit"]    = badLimit,
            },
            cancellationToken: cts.Token);

        var root = ParseRoot(result);
        Assert.False(root.GetProperty("ok").GetBoolean(),
            $"ok must be false for limit={badLimit} (out of 1–50 range).");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()),
            "error must not be empty.");
    }

    [Fact]
    public async Task SafeActionList_ApiHostNotConfigured_ReturnsOkFalse()
    {
        // When ApiHost:BaseUrl is empty (the default in test appsettings.json)
        // the tool must return ok=false rather than throwing an unhandled exception.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "safe_action_list",
            new Dictionary<string, object?>
            {
                ["tenantId"] = FakeTenantId,
                ["runId"]    = FakeRunId,
                ["limit"]    = 5,
            },
            cancellationToken: cts.Token);

        var root = ParseRoot(result);
        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false when ApiHost:BaseUrl is not configured.");

        var error = root.GetProperty("error").GetString() ?? string.Empty;
        Assert.Contains("ApiHost:BaseUrl", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Validation: safe_action_propose ───────────────────────────────────────

    [Fact]
    public async Task SafeActionPropose_EmptyTenantId_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "safe_action_propose",
            new Dictionary<string, object?>
            {
                ["tenantId"]            = "",
                ["runId"]               = FakeRunId,
                ["actionType"]          = "restart_service",
                ["proposedPayloadJson"] = "{}",
            },
            cancellationToken: cts.Token);

        var root = ParseRoot(result);
        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false for an empty tenantId.");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()),
            "error must not be empty.");
    }

    [Fact]
    public async Task SafeActionPropose_InvalidRunIdGuid_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "safe_action_propose",
            new Dictionary<string, object?>
            {
                ["tenantId"]            = FakeTenantId,
                ["runId"]               = "not-a-guid",
                ["actionType"]          = "restart_service",
                ["proposedPayloadJson"] = "{}",
            },
            cancellationToken: cts.Token);

        var root = ParseRoot(result);
        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false for an invalid runId GUID.");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()),
            "error must not be empty.");
    }

    [Fact]
    public async Task SafeActionPropose_EmptyActionType_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "safe_action_propose",
            new Dictionary<string, object?>
            {
                ["tenantId"]            = FakeTenantId,
                ["runId"]               = FakeRunId,
                ["actionType"]          = "",
                ["proposedPayloadJson"] = "{}",
            },
            cancellationToken: cts.Token);

        var root = ParseRoot(result);
        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false for an empty actionType.");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()),
            "error must not be empty.");
    }

    [Fact]
    public async Task SafeActionPropose_InvalidProposedPayloadJson_ReturnsOkFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "safe_action_propose",
            new Dictionary<string, object?>
            {
                ["tenantId"]            = FakeTenantId,
                ["runId"]               = FakeRunId,
                ["actionType"]          = "restart_service",
                ["proposedPayloadJson"] = "{ not valid json !!",
            },
            cancellationToken: cts.Token);

        var root = ParseRoot(result);
        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false for malformed proposedPayloadJson.");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()),
            "error must not be empty.");
    }

    [Fact]
    public async Task SafeActionPropose_ApiHostNotConfigured_ReturnsOkFalse()
    {
        // When ApiHost:BaseUrl is empty the tool must return ok=false
        // rather than throwing an unhandled exception.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var client = await CreateClientAsync(cts.Token);

        var result = await client.CallToolAsync(
            "safe_action_propose",
            new Dictionary<string, object?>
            {
                ["tenantId"]            = FakeTenantId,
                ["runId"]               = FakeRunId,
                ["actionType"]          = "restart_service",
                ["proposedPayloadJson"] = "{\"service\":\"api\"}",
            },
            cancellationToken: cts.Token);

        var root = ParseRoot(result);
        Assert.False(root.GetProperty("ok").GetBoolean(),
            "ok must be false when ApiHost:BaseUrl is not configured.");

        var error = root.GetProperty("error").GetString() ?? string.Empty;
        Assert.Contains("ApiHost:BaseUrl", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JsonElement ParseRoot(CallToolResult result)
    {
        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text),
            "Tool response must not be empty.");
        return JsonDocument.Parse(textBlock.Text).RootElement;
    }

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
