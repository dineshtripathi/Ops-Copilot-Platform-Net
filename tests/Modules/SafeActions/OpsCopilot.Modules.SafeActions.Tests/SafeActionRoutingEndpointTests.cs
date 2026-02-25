using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Application.Orchestration;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Enums;
using OpsCopilot.SafeActions.Domain.Repositories;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using OpsCopilot.SafeActions.Infrastructure.Validators;
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// HTTP-level integration tests for the routing executor pipeline.
/// Validates that actions route through the
/// <see cref="RoutingActionExecutor"/> to the correct downstream
/// executor depending on the feature-flag configuration:
/// <c>SafeActions:EnableRealHttpProbe</c> and
/// <c>SafeActions:EnableAzureReadExecutions</c>.
/// </summary>
public class SafeActionRoutingEndpointTests
{
    // ── Helper: test host with routing executor ─────────────────────

    private static async Task<(WebApplication App, HttpClient Client)> CreateRoutingHost(
        IActionRecordRepository repository,
        bool enableRealHttpProbe,
        HttpMessageHandler? handler = null,
        bool enableAzureRead = false,
        IAzureResourceReader? azureReader = null,
        bool enableAzureMonitorRead = false,
        IAzureMonitorLogsReader? azureMonitorReader = null)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SafeActions:EnableExecution"] = "True",
                ["SafeActions:EnableRealHttpProbe"] = enableRealHttpProbe.ToString(),
                ["SafeActions:HttpProbeTimeoutMs"] = "5000",
                ["SafeActions:HttpProbeMaxResponseBytes"] = "1024",
                ["SafeActions:EnableAzureReadExecutions"] = enableAzureRead.ToString(),
                ["SafeActions:AzureReadTimeoutMs"] = "5000",
                ["SafeActions:EnableAzureMonitorReadExecutions"] = enableAzureMonitorRead.ToString(),
                ["SafeActions:AzureMonitorQueryTimeoutMs"] = "5000",
            });

        // Register repository and policy
        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>(p =>
            p.EvaluateExecution(It.IsAny<string>(), It.IsAny<string>()) == PolicyDecision.Allow()));

        // Build the routing executor chain manually (no EF/SQL needed)
        var dryRun = new DryRunActionExecutor();
        var httpProbe = new HttpProbeActionExecutor(
            new HttpClient(handler ?? new FakeHandler(HttpStatusCode.OK, "probe-response")),
            new TargetUriValidator(),
            builder.Configuration,
            NullLogger<HttpProbeActionExecutor>.Instance);
        var azureGet = new AzureResourceGetActionExecutor(
            azureReader ?? Mock.Of<IAzureResourceReader>(),
            builder.Configuration,
            NullLogger<AzureResourceGetActionExecutor>.Instance);
        var azureMonitorQuery = new AzureMonitorQueryActionExecutor(
            azureMonitorReader ?? Mock.Of<IAzureMonitorLogsReader>(),
            builder.Configuration,
            NullLogger<AzureMonitorQueryActionExecutor>.Instance);

        var routing = new RoutingActionExecutor(
            dryRun,
            httpProbe,
            azureGet,
            azureMonitorQuery,
            builder.Configuration,
            NullLogger<RoutingActionExecutor>.Instance);

        builder.Services.AddSingleton<IActionExecutor>(routing);
        builder.Services.AddSingleton<SafeActionOrchestrator>();

        var app = builder.Build();
        app.MapSafeActionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static ActionRecord CreateApprovedProbeRecord(
        string payloadJson = "{\"url\":\"https://example.com\",\"method\":\"GET\"}")
    {
        var record = ActionRecord.Create(
            "t-routing", Guid.NewGuid(), "http_probe",
            payloadJson, null);
        record.Approve();
        return record;
    }

    private static ActionRecord CreateApprovedDryRunRecord(
        string payloadJson = "{\"target\":\"pod-1\"}")
    {
        var record = ActionRecord.Create(
            "t-routing", Guid.NewGuid(), "restart_pod",
            payloadJson, "{\"undo\":\"stop_pod\"}");
        record.Approve();
        return record;
    }

    private static ActionRecord CreateApprovedAzureGetRecord()
    {
        var payload = "{\"resourceId\":\"/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Compute/virtualMachines/vm-1\"}";
        var record = ActionRecord.Create(
            "t-routing", Guid.NewGuid(), "azure_resource_get",
            payload, null);
        record.Approve();
        return record;
    }

    private static ActionRecord CreateApprovedAzureMonitorQueryRecord()
    {
        var payload = JsonSerializer.Serialize(new
        {
            workspaceId = "00000000-0000-0000-0000-000000000000",
            query = "Heartbeat | take 5"
        });
        var record = ActionRecord.Create(
            "t-routing", Guid.NewGuid(), "azure_monitor_query",
            payload, null);
        record.Approve();
        return record;
    }

    private static Mock<IActionRecordRepository> CreateRepoMock(ActionRecord record)
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(
                It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repo;
    }

    // ── Execute: http_probe with real probe enabled ─────────────────

    [Fact]
    public async Task Execute_HttpProbe_Routes_To_RealProbe_WhenEnabled()
    {
        var record = CreateApprovedProbeRecord();
        var repo = CreateRepoMock(record);
        var handler = new FakeHandler(HttpStatusCode.OK, "health-ok");

        var (app, client) = await CreateRoutingHost(repo.Object, enableRealHttpProbe: true, handler);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("Completed", root.GetProperty("status").GetString());
            var outcomeJson = root.GetProperty("outcomeJson").GetString();
            Assert.NotNull(outcomeJson);

            using var outcome = JsonDocument.Parse(outcomeJson!);
            Assert.Equal("real_http_probe", outcome.RootElement.GetProperty("mode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Execute: http_probe falls back to dry-run when flag=false ────

    [Fact]
    public async Task Execute_HttpProbe_FallsBack_To_DryRun_WhenDisabled()
    {
        var record = CreateApprovedProbeRecord();
        var repo = CreateRepoMock(record);

        var (app, client) = await CreateRoutingHost(repo.Object, enableRealHttpProbe: false);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("Completed", root.GetProperty("status").GetString());
            var outcomeJson = root.GetProperty("outcomeJson").GetString();
            Assert.NotNull(outcomeJson);

            using var outcome = JsonDocument.Parse(outcomeJson!);
            Assert.Equal("dry-run", outcome.RootElement.GetProperty("mode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Execute: non-http_probe always goes to dry-run ──────────────

    [Fact]
    public async Task Execute_NonProbe_Routes_To_DryRun_EvenWhenProbeEnabled()
    {
        var record = CreateApprovedDryRunRecord();
        var repo = CreateRepoMock(record);

        var (app, client) = await CreateRoutingHost(repo.Object, enableRealHttpProbe: true);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var outcomeJson = doc.RootElement.GetProperty("outcomeJson").GetString()!;

            using var outcome = JsonDocument.Parse(outcomeJson);
            Assert.Equal("dry-run", outcome.RootElement.GetProperty("mode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Execute: azure_resource_get routes to azure when enabled ───

    [Fact]
    public async Task Execute_AzureResourceGet_Routes_To_AzureExecutor_WhenEnabled()
    {
        var record = CreateApprovedAzureGetRecord();
        var repo = CreateRepoMock(record);

        var readerMock = new Mock<IAzureResourceReader>();
        readerMock.Setup(r => r.GetResourceMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AzureResourceMetadata("vm-1", "Microsoft.Compute/virtualMachines", "eastus", "Succeeded", null, 2));

        var (app, client) = await CreateRoutingHost(
            repo.Object, enableRealHttpProbe: false,
            enableAzureRead: true, azureReader: readerMock.Object);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("Completed", root.GetProperty("status").GetString());
            var outcomeJson = root.GetProperty("outcomeJson").GetString();
            Assert.NotNull(outcomeJson);

            using var outcome = JsonDocument.Parse(outcomeJson!);
            Assert.Equal("azure_resource_get", outcome.RootElement.GetProperty("mode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Execute: azure_resource_get falls back to dry-run when disabled

    [Fact]
    public async Task Execute_AzureResourceGet_FallsBack_To_DryRun_WhenDisabled()
    {
        var record = CreateApprovedAzureGetRecord();
        var repo = CreateRepoMock(record);

        var (app, client) = await CreateRoutingHost(
            repo.Object, enableRealHttpProbe: false, enableAzureRead: false);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var outcomeJson = doc.RootElement.GetProperty("outcomeJson").GetString()!;

            using var outcome = JsonDocument.Parse(outcomeJson);
            Assert.Equal("dry-run", outcome.RootElement.GetProperty("mode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Execute: azure_monitor_query routes to real when enabled ──

    [Fact]
    public async Task Execute_AzureMonitorQuery_Routes_To_RealExecutor_WhenEnabled()
    {
        var record = CreateApprovedAzureMonitorQueryRecord();
        var repo = CreateRepoMock(record);

        var readerMock = new Mock<IAzureMonitorLogsReader>();
        readerMock.Setup(r => r.QueryLogsAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitorQueryResult(1, 2, "[{\"col\":\"val\"}]"));

        var (app, client) = await CreateRoutingHost(
            repo.Object, enableRealHttpProbe: false,
            enableAzureMonitorRead: true, azureMonitorReader: readerMock.Object);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("Completed", root.GetProperty("status").GetString());
            var outcomeJson = root.GetProperty("outcomeJson").GetString();
            Assert.NotNull(outcomeJson);

            using var outcome = JsonDocument.Parse(outcomeJson!);
            Assert.Equal("azure_monitor_query", outcome.RootElement.GetProperty("mode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Execute: azure_monitor_query fallback to dry-run when disabled

    [Fact]
    public async Task Execute_AzureMonitorQuery_FallsBack_To_DryRun_WhenDisabled()
    {
        var record = CreateApprovedAzureMonitorQueryRecord();
        var repo = CreateRepoMock(record);

        var (app, client) = await CreateRoutingHost(
            repo.Object, enableRealHttpProbe: false, enableAzureMonitorRead: false);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var outcomeJson = doc.RootElement.GetProperty("outcomeJson").GetString()!;

            using var outcome = JsonDocument.Parse(outcomeJson);
            Assert.Equal("dry-run", outcome.RootElement.GetProperty("mode").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Test doubles
    // ═══════════════════════════════════════════════════════════════

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
