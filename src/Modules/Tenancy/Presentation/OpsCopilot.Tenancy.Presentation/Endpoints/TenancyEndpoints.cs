using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.Tenancy.Application.Abstractions;

namespace OpsCopilot.Tenancy.Presentation.Endpoints;

public static class TenancyEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/tenants").WithTags("Tenancy");

        // POST /tenants — create a new tenant
        group.MapPost("/", async (
            HttpContext ctx,
            ITenantRegistry registry,
            CancellationToken ct) =>
        {
            var identity = ctx.Request.Headers["x-identity"].FirstOrDefault();
            var body = await ctx.Request.ReadFromJsonAsync<CreateTenantRequest>(ct);

            if (body is null || string.IsNullOrWhiteSpace(body.DisplayName))
                return Results.BadRequest(new { error = "displayName is required and must not be empty." });

            var tenant = await registry.CreateAsync(body.DisplayName.Trim(), identity, ct);
            return Results.Created($"/tenants/{tenant.TenantId}", new TenantResponse(
                tenant.TenantId, tenant.DisplayName, tenant.IsActive, tenant.CreatedAtUtc, tenant.UpdatedBy));
        })
        .WithName("CreateTenant")
        .Accepts<CreateTenantRequest>("application/json")
        .Produces<TenantResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        // GET /tenants — list all tenants
        group.MapGet("/", async (
            ITenantRegistry registry,
            CancellationToken ct) =>
        {
            var tenants = await registry.ListAsync(ct);
            var response = tenants.Select(t => new TenantResponse(
                t.TenantId, t.DisplayName, t.IsActive, t.CreatedAtUtc, t.UpdatedBy));
            return Results.Ok(response);
        })
        .WithName("ListTenants")
        .Produces<IEnumerable<TenantResponse>>(StatusCodes.Status200OK);

        // GET /tenants/{id} — get tenant by ID
        group.MapGet("/{id:guid}", async (
            Guid id,
            ITenantRegistry registry,
            CancellationToken ct) =>
        {
            var tenant = await registry.GetByIdAsync(id, ct);
            if (tenant is null) return Results.NotFound();

            return Results.Ok(new TenantResponse(
                tenant.TenantId, tenant.DisplayName, tenant.IsActive, tenant.CreatedAtUtc, tenant.UpdatedBy));
        })
        .WithName("GetTenant")
        .Produces<TenantResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // PUT /tenants/{id}/settings — upsert a config entry
        group.MapPut("/{id:guid}/settings", async (
            Guid id,
            HttpContext ctx,
            ITenantConfigStore configStore,
            ITenantRegistry registry,
            CancellationToken ct) =>
        {
            var tenant = await registry.GetByIdAsync(id, ct);
            if (tenant is null) return Results.NotFound();

            var identity = ctx.Request.Headers["x-identity"].FirstOrDefault();
            var body = await ctx.Request.ReadFromJsonAsync<UpsertSettingRequest>(ct);

            if (body is null || string.IsNullOrWhiteSpace(body.Key))
                return Results.BadRequest(new { error = "key is required." });
            if (body.Key.Length > 128)
                return Results.BadRequest(new { error = "key must be 128 characters or fewer." });
            if (body.Value is null)
                return Results.BadRequest(new { error = "value is required." });
            if (body.Value.Length > 1024)
                return Results.BadRequest(new { error = "value must be 1024 characters or fewer." });

            await configStore.UpsertAsync(id, body.Key.Trim(), body.Value, identity, ct);
            return Results.Ok(new { tenantId = id, key = body.Key.Trim(), value = body.Value });
        })
        .WithName("UpsertTenantSetting")
        .Accepts<UpsertSettingRequest>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // GET /tenants/{id}/settings — raw settings for a tenant
        group.MapGet("/{id:guid}/settings", async (
            Guid id,
            ITenantConfigStore configStore,
            ITenantRegistry registry,
            CancellationToken ct) =>
        {
            var tenant = await registry.GetByIdAsync(id, ct);
            if (tenant is null) return Results.NotFound();

            var entries = await configStore.GetAsync(id, ct);
            var response = entries.Select(e => new SettingResponse(
                e.Key, e.Value, e.UpdatedAtUtc, e.UpdatedBy));
            return Results.Ok(response);
        })
        .WithName("GetTenantSettings")
        .Produces<IEnumerable<SettingResponse>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // GET /tenants/{id}/settings/resolved — merged with governance defaults
        group.MapGet("/{id:guid}/settings/resolved", async (
            Guid id,
            ITenantConfigResolver resolver,
            ITenantRegistry registry,
            CancellationToken ct) =>
        {
            var tenant = await registry.GetByIdAsync(id, ct);
            if (tenant is null) return Results.NotFound();

            var effective = await resolver.ResolveAsync(id, ct);
            return Results.Ok(effective);
        })
        .WithName("GetTenantResolvedSettings")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }

    // ── Request / Response DTOs ──────────────────────────────────────────────

    public sealed record CreateTenantRequest(string DisplayName);

    public sealed record UpsertSettingRequest(string Key, string Value);

    public sealed record TenantResponse(
        Guid TenantId,
        string DisplayName,
        bool IsActive,
        DateTimeOffset CreatedAtUtc,
        string? UpdatedBy);

    public sealed record SettingResponse(
        string Key,
        string Value,
        DateTimeOffset UpdatedAtUtc,
        string? UpdatedBy);
}
