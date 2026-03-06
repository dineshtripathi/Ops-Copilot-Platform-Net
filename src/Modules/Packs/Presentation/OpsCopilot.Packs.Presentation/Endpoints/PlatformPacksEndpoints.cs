using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Presentation.Endpoints;

public static class PlatformPacksEndpoints
{
    public static IEndpointRouteBuilder MapPlatformPacksEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports/platform")
                       .WithTags("Platform Reporting");

        // ── Slice 34 — existing ───────────────────────────────────
        group.MapGet("/packs", GetPacks);

        // ── Slice 35 — catalog query endpoints (read-only) ───────
        group.MapGet("/packs/search", SearchPacks);
        group.MapGet("/packs/{name}", GetPackDetails);
        group.MapGet("/packs/{name}/runbooks", GetPackRunbooks);
        group.MapGet("/packs/{name}/evidence-collectors", GetPackEvidenceCollectors);
        group.MapGet("/packs/{name}/safe-actions", GetPackSafeActions);

        return app;
    }

    // ── GET /reports/platform/packs ─────────────────────────────────
    private static async Task<IResult> GetPacks(
        IPackCatalog catalog,
        CancellationToken ct)
    {
        var packs = await catalog.GetAllAsync(ct);

        var validCount = packs.Count(p => p.Validation.IsValid);

        var response = new
        {
            generatedAtUtc = DateTime.UtcNow,
            totalPacks = packs.Count,
            validPacks = validCount,
            invalidPacks = packs.Count - validCount,
            packs = packs.Select(p => new
            {
                name = p.Manifest.Name,
                version = p.Manifest.Version,
                minimumMode = p.Manifest.MinimumMode,
                resourceTypesCount = p.Manifest.ResourceTypes?.Count ?? 0,
                evidenceCollectorsCount = p.Manifest.EvidenceCollectors?.Count ?? 0,
                runbooksCount = p.Manifest.Runbooks?.Count ?? 0,
                safeActionsCount = p.Manifest.SafeActions?.Count ?? 0,
                isValid = p.Validation.IsValid,
                errors = p.Validation.Errors
            })
        };

        return Results.Ok(response);
    }

    // ── GET /reports/platform/packs/{name} ──────────────────────────
    private static async Task<IResult> GetPackDetails(
        string name,
        IPackCatalog catalog,
        CancellationToken ct)
    {
        var details = await catalog.GetDetailsAsync(name, ct);
        if (details is null)
            return Results.NotFound(new { error = $"Pack '{name}' not found." });

        return Results.Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            pack = new
            {
                details.Name,
                details.Version,
                details.Description,
                details.ResourceTypes,
                details.MinimumMode,
                details.EvidenceCollectorCount,
                details.RunbookCount,
                details.SafeActionCount,
                details.IsValid,
                details.Errors,
                details.PackPath
            }
        });
    }

    // ── GET /reports/platform/packs/search?resourceType=&minimumMode= ─
    private static async Task<IResult> SearchPacks(
        IPackCatalog catalog,
        CancellationToken ct,
        string? resourceType = null,
        string? minimumMode = null)
    {
        IReadOnlyList<Domain.Models.LoadedPack> results;

        if (!string.IsNullOrWhiteSpace(resourceType))
            results = await catalog.FindByResourceTypeAsync(resourceType, ct);
        else if (!string.IsNullOrWhiteSpace(minimumMode))
            results = await catalog.FindByMinimumModeAsync(minimumMode, ct);
        else
            results = await catalog.GetAllAsync(ct);

        return Results.Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            totalResults = results.Count,
            packs = results.Select(p => new
            {
                name = p.Manifest.Name,
                version = p.Manifest.Version,
                minimumMode = p.Manifest.MinimumMode,
                resourceTypes = p.Manifest.ResourceTypes,
                isValid = p.Validation.IsValid
            })
        });
    }

    // ── GET /reports/platform/packs/{name}/runbooks ─────────────────
    private static async Task<IResult> GetPackRunbooks(
        string name,
        IPackCatalog catalog,
        CancellationToken ct)
    {
        var runbooks = await catalog.GetRunbooksAsync(name, ct);
        if (runbooks is null)
            return Results.NotFound(new { error = $"Pack '{name}' not found." });

        return Results.Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            packName = name,
            totalRunbooks = runbooks.Count,
            runbooks = runbooks.Select(r => new { r.Id, r.File })
        });
    }

    // ── GET /reports/platform/packs/{name}/evidence-collectors ──────
    private static async Task<IResult> GetPackEvidenceCollectors(
        string name,
        IPackCatalog catalog,
        CancellationToken ct)
    {
        var collectors = await catalog.GetEvidenceCollectorsAsync(name, ct);
        if (collectors is null)
            return Results.NotFound(new { error = $"Pack '{name}' not found." });

        return Results.Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            packName = name,
            totalEvidenceCollectors = collectors.Count,
            evidenceCollectors = collectors.Select(e => new { e.Id, e.RequiredMode, e.QueryFile })
        });
    }

    // ── GET /reports/platform/packs/{name}/safe-actions ─────────────
    private static async Task<IResult> GetPackSafeActions(
        string name,
        IPackCatalog catalog,
        CancellationToken ct)
    {
        var actions = await catalog.GetSafeActionsAsync(name, ct);
        if (actions is null)
            return Results.NotFound(new { error = $"Pack '{name}' not found." });

        return Results.Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            packName = name,
            totalSafeActions = actions.Count,
            safeActions = actions.Select(s => new { s.Id, s.RequiresMode, s.DefinitionFile })
        });
    }
}
