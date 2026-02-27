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
using OpsCopilot.Tenancy.Presentation.Endpoints;
using Xunit;

namespace OpsCopilot.Modules.Tenancy.Tests;

public sealed class TenancyEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (WebApplication App, Mock<ITenantRegistry> Registry, Mock<ITenantConfigStore> Store, Mock<ITenantConfigResolver> Resolver) BuildApp()
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        var store = new Mock<ITenantConfigStore>(MockBehavior.Strict);
        var resolver = new Mock<ITenantConfigResolver>(MockBehavior.Strict);

        builder.Services.AddSingleton(registry.Object);
        builder.Services.AddSingleton(store.Object);
        builder.Services.AddSingleton(resolver.Object);

        var app = builder.Build();
        TenancyEndpoints.Map(app);
        return (app, registry, store, resolver);
    }

    private static Tenant MakeTenant(Guid? id = null, string name = "Contoso", string? updatedBy = null)
    {
        var tenant = Tenant.Create(name, updatedBy);
        // Use reflection to set TenantId for deterministic tests
        if (id.HasValue)
        {
            typeof(Tenant).GetProperty(nameof(Tenant.TenantId))!.SetValue(tenant, id.Value);
        }
        return tenant;
    }

    // ── POST /tenants ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTenant_Returns201_WithValidPayload()
    {
        var (app, registry, _, _) = BuildApp();

        registry
            .Setup(r => r.CreateAsync("Contoso", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(name: "Contoso"));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.PostAsJsonAsync("/tenants", new { DisplayName = "Contoso" });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.StartsWith("/tenants/", response.Headers.Location?.ToString());

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.Equal("Contoso", body.GetProperty("displayName").GetString());
            Assert.True(body.GetProperty("isActive").GetBoolean());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateTenant_Returns400_WhenDisplayNameIsEmpty()
    {
        var (app, _, _, _) = BuildApp();

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.PostAsJsonAsync("/tenants", new { DisplayName = "" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateTenant_Returns400_WhenBodyIsNull()
    {
        var (app, _, _, _) = BuildApp();

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.PostAsync("/tenants",
                new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateTenant_CapturesUpdatedBy_FromXIdentityHeader()
    {
        var (app, registry, _, _) = BuildApp();

        registry
            .Setup(r => r.CreateAsync("Fabrikam", "alice@ops", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(name: "Fabrikam", updatedBy: "alice@ops"));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "/tenants")
            {
                Content = JsonContent.Create(new { DisplayName = "Fabrikam" })
            };
            request.Headers.Add("x-identity", "alice@ops");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.Equal("alice@ops", body.GetProperty("updatedBy").GetString());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ── GET /tenants ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListTenants_Returns200_WithTenantList()
    {
        var (app, registry, _, _) = BuildApp();

        registry
            .Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { MakeTenant(name: "Alpha"), MakeTenant(name: "Bravo") });

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync("/tenants");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
            Assert.NotNull(items);
            Assert.Equal(2, items!.Length);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ── GET /tenants/{id} ────────────────────────────────────────────────────

    [Fact]
    public async Task GetTenant_Returns200_WhenTenantExists()
    {
        var (app, registry, _, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(id: tenantId, name: "Contoso"));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync($"/tenants/{tenantId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.Equal(tenantId.ToString(), body.GetProperty("tenantId").GetString());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetTenant_Returns404_WhenTenantDoesNotExist()
    {
        var (app, registry, _, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync($"/tenants/{tenantId}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ── PUT /tenants/{id}/settings ───────────────────────────────────────────

    [Fact]
    public async Task UpsertSetting_Returns200_WithValidPayload()
    {
        var (app, registry, store, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(id: tenantId));

        store
            .Setup(s => s.UpsertAsync(tenantId, "TriageEnabled", "true", "bob@ops", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Put, $"/tenants/{tenantId}/settings")
            {
                Content = JsonContent.Create(new { Key = "TriageEnabled", Value = "true" })
            };
            request.Headers.Add("x-identity", "bob@ops");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.Equal("TriageEnabled", body.GetProperty("key").GetString());
            Assert.Equal("true", body.GetProperty("value").GetString());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UpsertSetting_Returns400_WhenKeyIsEmpty()
    {
        var (app, registry, _, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(id: tenantId));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.PutAsJsonAsync(
                $"/tenants/{tenantId}/settings",
                new { Key = "", Value = "x" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UpsertSetting_Returns400_WhenKeyTooLong()
    {
        var (app, registry, _, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(id: tenantId));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var longKey = new string('k', 129);
            var response = await client.PutAsJsonAsync(
                $"/tenants/{tenantId}/settings",
                new { Key = longKey, Value = "x" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UpsertSetting_Returns400_WhenValueIsNull()
    {
        var (app, registry, _, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(id: tenantId));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            // Send JSON with null value
            var response = await client.PutAsync(
                $"/tenants/{tenantId}/settings",
                new StringContent("""{"Key":"Foo","Value":null}""", System.Text.Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UpsertSetting_Returns400_WhenValueTooLong()
    {
        var (app, registry, _, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(id: tenantId));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var longValue = new string('v', 1025);
            var response = await client.PutAsJsonAsync(
                $"/tenants/{tenantId}/settings",
                new { Key = "Foo", Value = longValue });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task UpsertSetting_Returns404_WhenTenantDoesNotExist()
    {
        var (app, registry, _, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.PutAsJsonAsync(
                $"/tenants/{tenantId}/settings",
                new { Key = "Foo", Value = "bar" });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ── GET /tenants/{id}/settings ───────────────────────────────────────────

    [Fact]
    public async Task GetSettings_Returns200_WithSettingsList()
    {
        var (app, registry, store, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(id: tenantId));

        store
            .Setup(s => s.GetAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantConfigEntry>
            {
                TenantConfigEntry.Create(tenantId, "TriageEnabled", "true", "ops@test"),
                TenantConfigEntry.Create(tenantId, "TokenBudget", "5000", "ops@test")
            });

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync($"/tenants/{tenantId}/settings");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
            Assert.NotNull(items);
            Assert.Equal(2, items!.Length);
            Assert.Equal("TriageEnabled", items[0].GetProperty("key").GetString());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetSettings_Returns404_WhenTenantDoesNotExist()
    {
        var (app, registry, _, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync($"/tenants/{tenantId}/settings");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ── GET /tenants/{id}/settings/resolved ──────────────────────────────────

    [Fact]
    public async Task GetResolvedSettings_Returns200_WithEffectiveConfig()
    {
        var (app, registry, _, resolver) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant(id: tenantId));

        resolver
            .Setup(r => r.ResolveAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveTenantConfig(
                AllowedTools: new List<string> { "restart-pod", "drain-node" },
                TriageEnabled: true,
                TokenBudget: 4000,
                SessionTtlMinutes: 45));

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync($"/tenants/{tenantId}/settings/resolved");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.True(body.GetProperty("triageEnabled").GetBoolean());
            Assert.Equal(4000, body.GetProperty("tokenBudget").GetInt32());
            Assert.Equal(45, body.GetProperty("sessionTtlMinutes").GetInt32());

            var tools = body.GetProperty("allowedTools");
            Assert.Equal(2, tools.GetArrayLength());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetResolvedSettings_Returns404_WhenTenantDoesNotExist()
    {
        var (app, registry, _, _) = BuildApp();
        var tenantId = Guid.NewGuid();

        registry
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync($"/tenants/{tenantId}/settings/resolved");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
