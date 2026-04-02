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

    private const string ValidVmssId =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test" +
        "/providers/Microsoft.Compute/virtualMachineScaleSets/vmss-1";

    private const string ValidAppConfigEndpoint = "https://ops-test.azconfig.io";

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Creates a RoutingActionExecutor with real downstream executors.</summary>
    private static RoutingActionExecutor CreateSut(
        bool enableRealHttpProbe,
        HttpMessageHandler? handler = null,
        bool enableAzureRead = false,
        IAzureResourceReader? azureReader = null,
        bool enableAzureMonitorRead = false,
        IAzureMonitorLogsReader? azureMonitorReader = null,
        bool enableArmWrite = false,
        IAzureVmWriter? armWriter = null,
        IAzureScaleWriter? scaleWriter = null,
        bool enableAppConfigWrite = false,
        IAppConfigFeatureFlagWriter? appConfigWriter = null)
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
                ["SafeActions:EnableArmWrite"] = enableArmWrite.ToString(),
                ["SafeActions:ArmWriteTimeoutMs"] = "5000",
                ["SafeActions:MaxArmScaleCapacity"] = "100",
                ["SafeActions:EnableAppConfigWrite"] = enableAppConfigWrite.ToString(),
                ["SafeActions:AppConfigWriteTimeoutMs"] = "5000",
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

        var armRestart = new ArmRestartActionExecutor(
            armWriter ?? Mock.Of<IAzureVmWriter>(),
            config,
            NullLogger<ArmRestartActionExecutor>.Instance);

        var armScale = new ArmScaleActionExecutor(
            scaleWriter ?? Mock.Of<IAzureScaleWriter>(),
            config,
            NullLogger<ArmScaleActionExecutor>.Instance);

        var appConfigFf = new AppConfigFeatureFlagExecutor(
            appConfigWriter ?? Mock.Of<IAppConfigFeatureFlagWriter>(),
            config,
            NullLogger<AppConfigFeatureFlagExecutor>.Instance);

        return new RoutingActionExecutor(
            dryRun,
            httpProbe,
            azureGet,
            azureMonitorQuery,
            armRestart,
            armScale,
            appConfigFf,
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
    //  Execute / Rollback: arm_restart routing (Slice 187)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_Routes_ArmRestart_To_Real_When_Enabled()
    {
        var writer = new Mock<IAzureVmWriter>(MockBehavior.Strict);
        writer.Setup(w => w.RestartAsync(ValidResourceId, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = CreateSut(enableRealHttpProbe: false, enableArmWrite: true, armWriter: writer.Object);
        var result = await sut.ExecuteAsync("arm_restart",
            $"{{\"resourceId\":\"{ValidResourceId}\"}}");

        Assert.True(result.Success);
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("arm_restart", doc.RootElement.GetProperty("mode").GetString());
        writer.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Routes_ArmRestart_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(enableRealHttpProbe: false, enableArmWrite: false);
        var result = await sut.ExecuteAsync("arm_restart",
            $"{{\"resourceId\":\"{ValidResourceId}\"}}");

        // Feature gate off → falls through to dry-run
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run", doc.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task RollbackAsync_Routes_ArmRestart_To_Real_When_Enabled()
    {
        var writer = new Mock<IAzureVmWriter>(MockBehavior.Strict);
        // Rollback always returns ROLLBACK_NOT_SUPPORTED without calling the writer
        var sut = CreateSut(enableRealHttpProbe: false, enableArmWrite: true, armWriter: writer.Object);
        var result = await sut.RollbackAsync("arm_restart",
            $"{{\"resourceId\":\"{ValidResourceId}\"}}");

        Assert.False(result.Success);
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("ROLLBACK_NOT_SUPPORTED", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RollbackAsync_Routes_ArmRestart_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(enableRealHttpProbe: false, enableArmWrite: false);
        var result = await sut.RollbackAsync("arm_restart",
            $"{{\"resourceId\":\"{ValidResourceId}\"}}");

        // Feature gate off → falls through to dry-run rollback
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback", doc.RootElement.GetProperty("mode").GetString());
    }

    // ═══════════════════════════════════════════════════════════════
    //  Execute / Rollback: arm_scale routing (Slice 188)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_Routes_ArmScale_To_Real_When_Enabled()
    {
        var writer = new Mock<IAzureScaleWriter>(MockBehavior.Strict);
        writer.Setup(w => w.GetCapacityAsync(ValidVmssId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(2);
        writer.Setup(w => w.SetCapacityAsync(ValidVmssId, 5, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableArmWrite: true,
            scaleWriter: writer.Object);

        var result = await sut.ExecuteAsync("arm_scale",
            $"{{\"resourceId\":\"{ValidVmssId}\",\"targetCapacity\":5}}");

        Assert.True(result.Success);
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("arm_scale", doc.RootElement.GetProperty("mode").GetString());
        writer.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Routes_ArmScale_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(enableRealHttpProbe: false, enableArmWrite: false);
        var result = await sut.ExecuteAsync("arm_scale",
            $"{{\"resourceId\":\"{ValidVmssId}\",\"targetCapacity\":3}}");

        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run", doc.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task RollbackAsync_Routes_ArmScale_To_Real_When_Enabled()
    {
        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableArmWrite: true,
            scaleWriter: Mock.Of<IAzureScaleWriter>());

        var result = await sut.RollbackAsync("arm_scale",
            $"{{\"resourceId\":\"{ValidVmssId}\"}}");

        Assert.False(result.Success);
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("ROLLBACK_NOT_SUPPORTED", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RollbackAsync_Routes_ArmScale_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(enableRealHttpProbe: false, enableArmWrite: false);
        var result = await sut.RollbackAsync("arm_scale",
            $"{{\"resourceId\":\"{ValidVmssId}\"}}");

        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback", doc.RootElement.GetProperty("mode").GetString());
    }

    // ═══════════════════════════════════════════════════════════════
    //  Execute / Rollback: app_config_feature_flag routing (Slice 189)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_Routes_AppConfigFf_To_Real_When_Enabled()
    {
        var writer = new Mock<IAppConfigFeatureFlagWriter>(MockBehavior.Strict);
        writer.Setup(w => w.GetEnabledAsync(ValidAppConfigEndpoint, "my-flag", It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);
        writer.Setup(w => w.SetEnabledAsync(ValidAppConfigEndpoint, "my-flag", true, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAppConfigWrite: true,
            appConfigWriter: writer.Object);

        var result = await sut.ExecuteAsync("app_config_feature_flag",
            $"{{\"endpoint\":\"{ValidAppConfigEndpoint}\",\"featureFlagId\":\"my-flag\",\"enabled\":true}}");

        Assert.True(result.Success);
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("app_config_feature_flag", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(ValidAppConfigEndpoint, doc.RootElement.GetProperty("endpoint").GetString());
        Assert.Equal("my-flag", doc.RootElement.GetProperty("featureFlagId").GetString());
        Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("previousEnabled").GetBoolean());
        writer.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Routes_AppConfigFf_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(enableRealHttpProbe: false, enableAppConfigWrite: false);
        var result = await sut.ExecuteAsync("app_config_feature_flag",
            $"{{\"endpoint\":\"{ValidAppConfigEndpoint}\",\"featureFlagId\":\"my-flag\",\"enabled\":true}}");

        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run", doc.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task RollbackAsync_Routes_AppConfigFf_To_Real_When_Enabled()
    {
        var writer = new Mock<IAppConfigFeatureFlagWriter>(MockBehavior.Strict);
        writer.Setup(w => w.SetEnabledAsync(ValidAppConfigEndpoint, "my-flag", false, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = CreateSut(
            enableRealHttpProbe: false,
            enableAppConfigWrite: true,
            appConfigWriter: writer.Object);

        var result = await sut.RollbackAsync("app_config_feature_flag",
            $"{{\"endpoint\":\"{ValidAppConfigEndpoint}\",\"featureFlagId\":\"my-flag\",\"enabled\":false}}");

        Assert.True(result.Success);
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("app_config_feature_flag_rollback", doc.RootElement.GetProperty("mode").GetString());
        writer.VerifyAll();
    }

    [Fact]
    public async Task RollbackAsync_Routes_AppConfigFf_To_DryRun_When_Disabled()
    {
        var sut = CreateSut(enableRealHttpProbe: false, enableAppConfigWrite: false);
        var result = await sut.RollbackAsync("app_config_feature_flag",
            $"{{\"endpoint\":\"{ValidAppConfigEndpoint}\",\"featureFlagId\":\"my-flag\",\"enabled\":false}}");

        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback", doc.RootElement.GetProperty("mode").GetString());
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
