using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.DTOs;
using OpsCopilot.Tenancy.Domain.Entities;
using OpsCopilot.Tenancy.Domain.Enums;
using OpsCopilot.Tenancy.Presentation.Endpoints;
using Xunit;

namespace OpsCopilot.Modules.Tenancy.Tests;

public sealed class OnboardingEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static (WebApplication App,
                    Mock<ITenantRegistry> Registry,
                    Mock<IOnboardingOrchestrator> Orchestrator) BuildApp()
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        var orchestrator = new Mock<IOnboardingOrchestrator>(MockBehavior.Strict);

        // also register unused services that TenancyEndpoints.Map resolves from DI
        var configStore = new Mock<ITenantConfigStore>(MockBehavior.Strict);
        var resolver = new Mock<ITenantConfigResolver>(MockBehavior.Strict);

        builder.Services.AddSingleton(registry.Object);
        builder.Services.AddSingleton(orchestrator.Object);
        builder.Services.AddSingleton(configStore.Object);
        builder.Services.AddSingleton(resolver.Object);

        var app = builder.Build();
        TenancyEndpoints.Map(app);
        return (app, registry, orchestrator);
    }

    private static Tenant MakeTenant(Guid id, bool active = true)
    {
        var tenant = Tenant.Create("Contoso", null);
        typeof(Tenant).GetProperty(nameof(Tenant.TenantId))!.SetValue(tenant, id);
        if (!active)
        {
            var deactivate = typeof(Tenant).GetMethod("Deactivate");
            if (deactivate is not null)
                deactivate.Invoke(tenant, new object?[] { null });
            else
                typeof(Tenant).GetProperty(nameof(Tenant.IsActive))!.SetValue(tenant, false);
        }
        return tenant;
    }

    [Fact]
    public async Task OnboardTenant_Returns202_WhenOrchestrationSucceeds()
    {
        var tenantId = Guid.NewGuid();
        var (app, registry, orchestrator) = BuildApp();

        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeTenant(tenantId));

        orchestrator
            .Setup(o => o.OnboardAsync(It.Is<OnboardingRequest>(r => r.TenantId == tenantId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnboardingResult(tenantId, OnboardingStatus.Completed,
                new[] { "TenantLookup", "ActivationCheck" }));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.PostAsync($"/tenants/{tenantId}/onboard", null);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.Equal(tenantId.ToString(), body.GetProperty("tenantId").GetString());
            Assert.Equal("Completed", body.GetProperty("status").GetString());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task OnboardTenant_Returns404_WhenTenantNotFound()
    {
        var tenantId = Guid.NewGuid();
        var (app, registry, orchestrator) = BuildApp();

        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Tenant?)null);

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.PostAsync($"/tenants/{tenantId}/onboard", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task OnboardTenant_Returns422_WhenOrchestrationFails()
    {
        var tenantId = Guid.NewGuid();
        var (app, registry, orchestrator) = BuildApp();

        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeTenant(tenantId));

        orchestrator
            .Setup(o => o.OnboardAsync(It.Is<OnboardingRequest>(r => r.TenantId == tenantId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnboardingResult(tenantId, OnboardingStatus.Failed,
                Array.Empty<string>(), FailedStep: "TenantLookup", ErrorMessage: "Tenant not found."));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.PostAsync($"/tenants/{tenantId}/onboard", null);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.Equal("Tenant not found.", body.GetProperty("error").GetString());
            Assert.Equal("TenantLookup", body.GetProperty("failedStep").GetString());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task OnboardTenant_PassesIdentityHeader_ToOrchestrator()
    {
        var tenantId = Guid.NewGuid();
        string? capturedIdentity = null;
        var (app, registry, orchestrator) = BuildApp();

        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeTenant(tenantId));

        orchestrator
            .Setup(o => o.OnboardAsync(It.IsAny<OnboardingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<OnboardingRequest, CancellationToken>((req, _) => capturedIdentity = req.RequestedBy)
            .ReturnsAsync(new OnboardingResult(tenantId, OnboardingStatus.Completed,
                new[] { "TenantLookup", "ActivationCheck" }));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Post, $"/tenants/{tenantId}/onboard");
            request.Headers.Add("x-identity", "ops-admin@contoso.com");
            await client.SendAsync(request);

            Assert.Equal("ops-admin@contoso.com", capturedIdentity);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
