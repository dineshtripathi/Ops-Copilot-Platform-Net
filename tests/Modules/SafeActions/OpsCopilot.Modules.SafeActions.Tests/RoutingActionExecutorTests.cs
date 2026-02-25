using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using OpsCopilot.SafeActions.Infrastructure.Validators;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Unit tests for <see cref="RoutingActionExecutor"/>.
/// Verifies correct routing based on feature-flag configuration:
///   azure_resource_get, azure_monitor_query, http_probe, and dry-run fallback.
/// </summary>
public class RoutingActionExecutorTests
{
    private const string ValidResourceId =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Compute/virtualMachines/vm-1";

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Creates a RoutingActionExecutor with real downstream executors.</summary>
    private static RoutingActionExecutor CreateSut(
        bool enableRealHttpProbe,
        HttpMessageHandler? handler = null,
        bool enableAzureRead = false,
        IAzureResourceReader? azureReader = null,
        bool enableAzureMonitorRead = false,
        IAzureMonitorLogsReader? azureMonitorReader = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SafeActions:EnableRealHttpProbe"] = enableRealHttpProbe.ToString(),
                ["SafeActions:HttpProbeTimeoutMs"] = "5000",
                ["SafeActions:HttpProbeMaxResponseBytes"] = "1024",
                ["SafeActions:EnableAzureReadExecutions"] = enableAzureRead.ToString(),
                ["SafeActions:AzureReadTimeoutMs"] = "5000",
                ["SafeActions:EnableAzureMonitorReadExecutions"] = enableAzureMonitorRead.ToString(),
                ["SafeActions:AzureMonitorQueryTimeoutMs"] = "5000",
            })
            .Build();

        var dryRun = new DryRunActionExecutor();

        var httpProbe = new HttpProbeActionExecutor(
            new HttpClient(handler ?? new FakeHandler(HttpStatusCode.OK, "probe-response")),
            new TargetUriValidator(),
            config,
            NullLogger<HttpProbeActionExecutor>.Instance);

        var azureGet = new AzureResourceGetActionExecutor(
            azureReader ?? Mock.Of<IAzureResourceReader>(),
            config,
            NullLogger<AzureResourceGetActionExecutor>.Instance);

        var azureMonitorQuery = new AzureMonitorQueryActionExecutor(
            azureMonitorReader ?? Mock.Of<IAzureMonitorLogsReader>(),
            config,
            NullLogger<AzureMonitorQueryActionExecutor>.Instance);

        return new RoutingActionExecutor(
            dryRun,
            httpProbe,
            azureGet,
            azureMonitorQuery,
            config,
            NullLogger<RoutingActionExecutor>.Instance);
    }

    // ── Execute: http_probe + flag enabled → real probe ─────────────

    [Fact]
    public async Task ExecuteAsync_Routes_HttpProbe_To_RealProbe_When_Enabled()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "real");
        var sut = CreateSut(enableRealHttpProbe: true, handler: handler);

        var payload = JsonSerializer.Serialize(new { url = "https://example.com", method = "GET" });
        var result = await sut.ExecuteAsync("http_probe", payload, CancellationToken.None);

        Assert.True(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("real_http_probe", json.RootElement.GetProperty("mode").GetString());
    }

    // ── Execute: http_probe + flag disabled → dry-run ───────────────

    [Fact]
    public async Task ExecuteAsync_Routes_HttpProbe_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(enableRealHttpProbe: false);

        var payload = JsonSerializer.Serialize(new { url = "https://example.com" });
        var result = await sut.ExecuteAsync("http_probe", payload, CancellationToken.None);

        Assert.True(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run", json.RootElement.GetProperty("mode").GetString());
    }

    // ── Execute: non-http_probe action always goes to dry-run ───────

    [Theory]
    [InlineData("restart_service")]
    [InlineData("scale_out")]
    [InlineData("dns_update")]
    public async Task ExecuteAsync_Routes_NonProbe_To_DryRun_Regardless_Of_Flag(string actionType)
    {
        var sut = CreateSut(enableRealHttpProbe: true);

        var payload = JsonSerializer.Serialize(new { target = "svc-1" });
        var result = await sut.ExecuteAsync(actionType, payload, CancellationToken.None);

        Assert.True(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run", json.RootElement.GetProperty("mode").GetString());
    }

    // ── Execute: case-insensitive action type matching ───────────────

    [Theory]
    [InlineData("HTTP_PROBE")]
    [InlineData("Http_Probe")]
    public async Task ExecuteAsync_Routes_HttpProbe_CaseInsensitive(string actionType)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "ok");
        var sut = CreateSut(enableRealHttpProbe: true, handler: handler);

        var payload = JsonSerializer.Serialize(new { url = "https://example.com", method = "GET" });
        var result = await sut.ExecuteAsync(actionType, payload, CancellationToken.None);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("real_http_probe", json.RootElement.GetProperty("mode").GetString());
    }

    // ── Rollback: http_probe + flag enabled → real probe rollback ───

    [Fact]
    public async Task RollbackAsync_Routes_HttpProbe_To_RealProbe_When_Enabled()
    {
        var sut = CreateSut(enableRealHttpProbe: true);

        var result = await sut.RollbackAsync("http_probe", "{}", CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Contains("not supported", json.RootElement.GetProperty("reason").GetString());
    }

    // ── Rollback: http_probe + flag disabled → dry-run rollback ─────

    [Fact]
    public async Task RollbackAsync_Routes_HttpProbe_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(enableRealHttpProbe: false);

        var payload = JsonSerializer.Serialize(new { target = "svc-1" });
        var result = await sut.RollbackAsync("http_probe", payload, CancellationToken.None);

        Assert.True(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback", json.RootElement.GetProperty("mode").GetString());
    }

    // ── Rollback: non-http_probe → dry-run rollback ─────────────────

    [Theory]
    [InlineData("restart_service")]
    [InlineData("scale_out")]
    public async Task RollbackAsync_Routes_NonProbe_To_DryRun(string actionType)
    {
        var sut = CreateSut(enableRealHttpProbe: true);

        var payload = JsonSerializer.Serialize(new { target = "svc-1" });
        var result = await sut.RollbackAsync(actionType, payload, CancellationToken.None);

        Assert.True(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback", json.RootElement.GetProperty("mode").GetString());
    }

    // ── Execute: azure_resource_get + flag enabled → real azure ────

    [Fact]
    public async Task ExecuteAsync_Routes_AzureGet_To_RealAzure_When_Enabled()
    {
        var metadata = new AzureResourceMetadata(
            "vm-1", "Microsoft.Compute/virtualMachines",
            "eastus", "Succeeded", "etag-1", 2);

        var reader = new Mock<IAzureResourceReader>(MockBehavior.Strict);
        reader.Setup(r => r.GetResourceMetadataAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAzureRead: true,
            azureReader: reader.Object);

        var payload = JsonSerializer.Serialize(new { resourceId = ValidResourceId });
        var result = await sut.ExecuteAsync(
            "azure_resource_get", payload, CancellationToken.None);

        Assert.True(result.Success);
        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("azure_resource_get",
            json.RootElement.GetProperty("mode").GetString());
    }

    // ── Execute: azure_resource_get + flag disabled → dry-run ───────

    [Fact]
    public async Task ExecuteAsync_Routes_AzureGet_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAzureRead: false);

        var payload = JsonSerializer.Serialize(new { resourceId = ValidResourceId });
        var result = await sut.ExecuteAsync(
            "azure_resource_get", payload, CancellationToken.None);

        Assert.True(result.Success);
        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run",
            json.RootElement.GetProperty("mode").GetString());
    }

    // ── Execute: azure_resource_get case-insensitive ─────────────────

    [Theory]
    [InlineData("AZURE_RESOURCE_GET")]
    [InlineData("Azure_Resource_Get")]
    public async Task ExecuteAsync_Routes_AzureGet_CaseInsensitive(string actionType)
    {
        var metadata = new AzureResourceMetadata(
            "vm-1", "Microsoft.Compute/virtualMachines",
            "eastus", "Succeeded", null, 0);

        var reader = new Mock<IAzureResourceReader>(MockBehavior.Strict);
        reader.Setup(r => r.GetResourceMetadataAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAzureRead: true,
            azureReader: reader.Object);

        var payload = JsonSerializer.Serialize(new { resourceId = ValidResourceId });
        var result = await sut.ExecuteAsync(actionType, payload, CancellationToken.None);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("azure_resource_get",
            json.RootElement.GetProperty("mode").GetString());
    }

    // ── Rollback: azure_resource_get + flag enabled → not supported ──

    [Fact]
    public async Task RollbackAsync_Routes_AzureGet_To_Azure_When_Enabled()
    {
        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAzureRead: true,
            azureReader: Mock.Of<IAzureResourceReader>());

        var result = await sut.RollbackAsync(
            "azure_resource_get", "{}", CancellationToken.None);

        Assert.False(result.Success);
        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Contains("not supported",
            json.RootElement.GetProperty("reason").GetString());
    }

    // ── Rollback: azure_resource_get + flag disabled → dry-run ──────

    [Fact]
    public async Task RollbackAsync_Routes_AzureGet_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAzureRead: false);

        var payload = JsonSerializer.Serialize(new { target = "svc-1" });
        var result = await sut.RollbackAsync(
            "azure_resource_get", payload, CancellationToken.None);

        Assert.True(result.Success);
        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback",
            json.RootElement.GetProperty("mode").GetString());
    }

    // ── Execute: azure_monitor_query + flag enabled → real query ────

    [Fact]
    public async Task ExecuteAsync_Routes_AzureMonitorQuery_To_Real_When_Enabled()
    {
        var queryResult = new MonitorQueryResult(1, 2, """[{"col":"val"}]""");
        var reader = new Mock<IAzureMonitorLogsReader>(MockBehavior.Strict);
        reader.Setup(r => r.QueryLogsAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryResult);

        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAzureMonitorRead: true,
            azureMonitorReader: reader.Object);

        var payload = JsonSerializer.Serialize(new
        {
            workspaceId = "00000000-0000-0000-0000-000000000000",
            query = "Heartbeat | take 5"
        });
        var result = await sut.ExecuteAsync(
            "azure_monitor_query", payload, CancellationToken.None);

        Assert.True(result.Success);
        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("azure_monitor_query",
            json.RootElement.GetProperty("mode").GetString());
    }

    // ── Execute: azure_monitor_query + flag disabled → dry-run ──────

    [Fact]
    public async Task ExecuteAsync_Routes_AzureMonitorQuery_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAzureMonitorRead: false);

        var payload = JsonSerializer.Serialize(new
        {
            workspaceId = "00000000-0000-0000-0000-000000000000",
            query = "Heartbeat | take 5"
        });
        var result = await sut.ExecuteAsync(
            "azure_monitor_query", payload, CancellationToken.None);

        Assert.True(result.Success);
        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run",
            json.RootElement.GetProperty("mode").GetString());
    }

    // ── Execute: azure_monitor_query case-insensitive ────────────────

    [Theory]
    [InlineData("AZURE_MONITOR_QUERY")]
    [InlineData("Azure_Monitor_Query")]
    public async Task ExecuteAsync_Routes_AzureMonitorQuery_CaseInsensitive(string actionType)
    {
        var queryResult = new MonitorQueryResult(0, 0, "[]");
        var reader = new Mock<IAzureMonitorLogsReader>(MockBehavior.Strict);
        reader.Setup(r => r.QueryLogsAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryResult);

        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAzureMonitorRead: true,
            azureMonitorReader: reader.Object);

        var payload = JsonSerializer.Serialize(new
        {
            workspaceId = "00000000-0000-0000-0000-000000000000",
            query = "Heartbeat"
        });
        var result = await sut.ExecuteAsync(actionType, payload, CancellationToken.None);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("azure_monitor_query",
            json.RootElement.GetProperty("mode").GetString());
    }

    // ── Rollback: azure_monitor_query + enabled → not supported ─────

    [Fact]
    public async Task RollbackAsync_Routes_AzureMonitorQuery_To_Real_When_Enabled()
    {
        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAzureMonitorRead: true,
            azureMonitorReader: Mock.Of<IAzureMonitorLogsReader>());

        var result = await sut.RollbackAsync(
            "azure_monitor_query", "{}", CancellationToken.None);

        Assert.False(result.Success);
        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("not_supported",
            json.RootElement.GetProperty("reason").GetString());
    }

    // ── Rollback: azure_monitor_query + disabled → dry-run ──────────

    [Fact]
    public async Task RollbackAsync_Routes_AzureMonitorQuery_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAzureMonitorRead: false);

        var payload = JsonSerializer.Serialize(new { target = "svc-1" });
        var result = await sut.RollbackAsync(
            "azure_monitor_query", payload, CancellationToken.None);

        Assert.True(result.Success);
        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback",
            json.RootElement.GetProperty("mode").GetString());
    }

    // ═══════════════════════════════════════════════════════════════
    //  Test doubles
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Returns a fixed HTTP response.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public FakeHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body)
            });
        }
    }
}
