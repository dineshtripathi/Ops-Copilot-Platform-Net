using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.AlertIngestion.Application.Handlers;
using OpsCopilot.AlertIngestion.Application.Normalizers;
using OpsCopilot.AlertIngestion.Application.Services;
using OpsCopilot.AlertIngestion.Presentation.Contracts;
using OpsCopilot.AlertIngestion.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.AgentRuns;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using Xunit;

namespace OpsCopilot.Integration.Tests;

/// <summary>
/// E2E tests for alert ingestion: POST /ingest/alert through the full
/// normalizer → handler → dispatcher pipeline with in-memory TestServer.
/// </summary>
public sealed class AlertIngestionE2ETests
{
    private static async Task<(WebApplication App, HttpClient Client,
        Mock<IAgentRunCreator> RunCreator, Mock<IAlertTriageDispatcher> Dispatcher)>
        CreateTestHost()
    {
        var runCreator = new Mock<IAgentRunCreator>(MockBehavior.Strict);
        runCreator
            .Setup(r => r.FindRecentSessionIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        runCreator
            .Setup(r => r.CreateRunAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<AlertRunContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var dispatcher = new Mock<IAlertTriageDispatcher>(MockBehavior.Loose);
        dispatcher
            .Setup(d => d.DispatchAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sessionPolicy = new Mock<ISessionPolicy>(MockBehavior.Loose);
        sessionPolicy
            .Setup(p => p.GetSessionTtl(It.IsAny<string>()))
            .Returns(TimeSpan.FromMinutes(120));

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddAuthentication(E2ETestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, E2ETestAuthHandler>(
                E2ETestAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization();

        builder.Services.AddSingleton<IAlertNormalizer, AzureMonitorAlertNormalizer>();
        builder.Services.AddSingleton<IAlertNormalizer, DatadogAlertNormalizer>();
        builder.Services.AddSingleton<IAlertNormalizer, GenericAlertNormalizer>();
        builder.Services.AddSingleton<AlertNormalizerRouter>();
        builder.Services.AddSingleton(runCreator.Object);
        builder.Services.AddSingleton(dispatcher.Object);
        builder.Services.AddSingleton(sessionPolicy.Object);
        builder.Services.AddScoped<IngestAlertCommandHandler>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAlertIngestionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient(), runCreator, dispatcher);
    }

    [Fact]
    public async Task Post_alert_with_valid_payload_returns_200_and_dispatches()
    {
        var (app, client, _, dispatcher) = await CreateTestHost();
        try
        {
            var payload = """
            {
                "schemaId": "azureMonitorCommonAlertSchema",
                "data": {
                    "essentials": {
                        "alertId": "/subscriptions/00000000-0000-0000-0000-000000000001/providers/Microsoft.AlertsManagement/alerts/test-alert-001",
                        "alertRule": "HighCpuUsage",
                        "severity": "Sev1",
                        "monitorCondition": "Fired",
                        "monitoringService": "Platform",
                        "signalType": "Metric",
                        "firedDateTime": "2025-01-15T10:30:00Z",
                        "description": "CPU usage exceeded 90%",
                        "alertTargetIDs": ["/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/vm-web-01"]
                    },
                    "alertContext": {}
                }
            }
            """;

            var request = new HttpRequestMessage(HttpMethod.Post, "/ingest/alert")
            {
                Content = JsonContent.Create(new IngestAlertRequest("azure_monitor", payload))
            };
            request.Headers.Add("x-tenant-id", "tenant-e2e-001");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<IngestAlertResponse>();
            Assert.NotNull(body);
            Assert.NotEqual(Guid.Empty, body!.RunId);
            Assert.False(string.IsNullOrEmpty(body.Fingerprint));
            Assert.True(body.Dispatched);

            dispatcher.Verify(
                d => d.DispatchAsync("tenant-e2e-001", It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Post_alert_without_tenant_header_returns_400()
    {
        var (app, client, _, _) = await CreateTestHost();
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/ingest/alert")
            {
                Content = JsonContent.Create(new IngestAlertRequest("azure_monitor", """{"schemaId":"azureMonitorCommonAlertSchema","data":{"essentials":{"alertId":"a","alertRule":"r","severity":"Sev1","monitorCondition":"Fired","monitoringService":"Platform","signalType":"Metric","firedDateTime":"2025-01-01T00:00:00Z","description":"d","alertTargetIDs":["/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm"]},"alertContext":{}}}"""))
            };
            // Deliberately omit x-tenant-id header

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
