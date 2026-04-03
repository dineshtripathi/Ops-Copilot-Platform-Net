using Microsoft.Extensions.Logging;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.DTOs;
using OpsCopilot.Tenancy.Domain.Enums;

namespace OpsCopilot.Tenancy.Infrastructure.Services;

public sealed class TenantOnboardingOrchestrator : IOnboardingOrchestrator
{
    private readonly ITenantRegistry _registry;
    private readonly ILogger<TenantOnboardingOrchestrator> _logger;
    private readonly IResourceDiscoveryService? _discoveryService;
    private readonly IConnectorHealthValidator? _connectorHealthValidator;
    private readonly IOnboardingBaselineSeeder? _baselineSeeder;

    public TenantOnboardingOrchestrator(
        ITenantRegistry registry,
        ILogger<TenantOnboardingOrchestrator> logger,
        IResourceDiscoveryService? discoveryService = null,
        IConnectorHealthValidator? connectorHealthValidator = null,
        IOnboardingBaselineSeeder? baselineSeeder = null)
    {
        _registry                 = registry;
        _logger                   = logger;
        _discoveryService         = discoveryService;
        _connectorHealthValidator = connectorHealthValidator;
        _baselineSeeder           = baselineSeeder;
    }

    public async Task<OnboardingResult> OnboardAsync(
        OnboardingRequest request,
        CancellationToken ct = default)
    {
        var steps = new List<string>();

        // Step 1 — verify tenant exists
        var tenant = await _registry.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
        {
            _logger.LogWarning("Onboarding failed — tenant {TenantId} not found", request.TenantId);
            return new OnboardingResult(
                request.TenantId,
                OnboardingStatus.Failed,
                steps,
                FailedStep: "TenantLookup",
                ErrorMessage: "Tenant not found.");
        }

        steps.Add("TenantLookup");

        // Step 2 — verify tenant is active
        if (!tenant.IsActive)
        {
            _logger.LogWarning("Onboarding failed — tenant {TenantId} is not active", request.TenantId);
            return new OnboardingResult(
                request.TenantId,
                OnboardingStatus.Failed,
                steps,
                FailedStep: "ActivationCheck",
                ErrorMessage: "Tenant is not active.");
        }

        steps.Add("ActivationCheck");

        // Step 3 — resource discovery (§6.19)
        if (_discoveryService is not null)
        {
            var summary = await _discoveryService.DiscoverAsync(request.TenantId, ct);
            _logger.LogInformation(
                "Resource discovery for tenant {TenantId}: {Count} resource(s), {ConnectorCount} connector(s) detected",
                request.TenantId, summary.DiscoveredResourceCount, summary.DetectedConnectors.Count);
            steps.Add("ResourceDiscovery");
        }

        // Step 4 — connector health validation (§6.19)
        if (_connectorHealthValidator is not null)
        {
            var unhealthy = await _connectorHealthValidator.ValidateConnectorsAsync(request.TenantId, ct);
            if (unhealthy.Count > 0)
            {
                _logger.LogWarning(
                    "Onboarding failed — {Count} unhealthy connector(s) for tenant {TenantId}: {Names}",
                    unhealthy.Count, request.TenantId, string.Join(", ", unhealthy));
                return new OnboardingResult(
                    request.TenantId,
                    OnboardingStatus.Failed,
                    steps,
                    FailedStep: "ConnectorHealthValidation",
                    ErrorMessage: $"Unhealthy connectors: {string.Join(", ", unhealthy)}");
            }

            steps.Add("ConnectorHealthValidation");
        }

        // Step 5 — baseline seeding (§6.19)
        if (_baselineSeeder is not null)
        {
            var seededKeys = await _baselineSeeder.SeedAsync(request.TenantId, request.RequestedBy, ct);
            _logger.LogInformation(
                "Baseline seeded for tenant {TenantId}: {Keys}",
                request.TenantId, string.Join(", ", seededKeys));
            steps.Add("BaselineSeeding");
        }

        _logger.LogInformation("Tenant {TenantId} onboarded by {RequestedBy}",
            request.TenantId, request.RequestedBy ?? "system");

        return new OnboardingResult(request.TenantId, OnboardingStatus.Completed, steps);
    }
}
