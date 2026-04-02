using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.Configuration;
using OpsCopilot.Tenancy.Application.DTOs;
using OpsCopilot.Tenancy.Domain.Entities;
using OpsCopilot.Tenancy.Domain.Enums;
using OpsCopilot.Tenancy.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.Tenancy.Tests;

// ─── NullResourceDiscoveryService ────────────────────────────────────────────

public sealed class NullResourceDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_Returns_EmptySummary_WithCorrectTenantId()
    {
        var tenantId = Guid.NewGuid();
        var sut      = new NullResourceDiscoveryService();

        var result = await sut.DiscoverAsync(tenantId);

        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(0, result.DiscoveredResourceCount);
        Assert.Empty(result.DetectedConnectors);
    }

    [Fact]
    public async Task DiscoverAsync_Returns_EmptyConnectorList()
    {
        var sut    = new NullResourceDiscoveryService();
        var result = await sut.DiscoverAsync(Guid.NewGuid());

        Assert.IsAssignableFrom<IReadOnlyList<string>>(result.DetectedConnectors);
    }

    [Fact]
    public async Task DiscoverAsync_CompletesSynchronously_WithCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var sut       = new NullResourceDiscoveryService();

        var result = await sut.DiscoverAsync(Guid.NewGuid(), cts.Token);

        Assert.NotNull(result);
    }
}

// ─── NullConnectorHealthValidator ────────────────────────────────────────────

public sealed class NullConnectorHealthValidatorTests
{
    [Fact]
    public async Task ValidateConnectorsAsync_Returns_EmptyList()
    {
        var sut    = new NullConnectorHealthValidator();
        var result = await sut.ValidateConnectorsAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task ValidateConnectorsAsync_ReturnsReadOnlyList()
    {
        var sut    = new NullConnectorHealthValidator();
        var result = await sut.ValidateConnectorsAsync(Guid.NewGuid());

        Assert.IsAssignableFrom<IReadOnlyList<string>>(result);
    }
}

// ─── GovernanceDefaultsBaselineSeeder ────────────────────────────────────────

public sealed class GovernanceDefaultsBaselineSeederTests
{
    private static IOptions<GovernanceDefaultsConfig> Options(GovernanceDefaultsConfig cfg)
        => Microsoft.Extensions.Options.Options.Create(cfg);

    [Fact]
    public async Task SeedAsync_Upserts_AllowedTools_And_CoreKeys()
    {
        var tenantId = Guid.NewGuid();
        var store    = new Mock<ITenantConfigStore>(MockBehavior.Strict);
        var defaults = new GovernanceDefaultsConfig
        {
            AllowedTools      = new List<string> { "restart", "scale" },
            TriageEnabled     = true,
            TokenBudget       = 4000,
            SessionTtlMinutes = 60
        };

        // Expect 4 upsert calls (AllowedTools, TriageEnabled, TokenBudget, SessionTtlMinutes)
        store.Setup(s => s.UpsertAsync(tenantId, It.IsAny<string>(), It.IsAny<string>(), "admin", It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var sut    = new GovernanceDefaultsBaselineSeeder(store.Object, Options(defaults));
        var seeded = await sut.SeedAsync(tenantId, "admin");

        Assert.Contains("AllowedTools",      seeded);
        Assert.Contains("TriageEnabled",     seeded);
        Assert.Contains("TokenBudget",       seeded);
        Assert.Contains("SessionTtlMinutes", seeded);
    }

    [Fact]
    public async Task SeedAsync_OmitsTokenBudget_WhenNull()
    {
        var tenantId = Guid.NewGuid();
        var store    = new Mock<ITenantConfigStore>(MockBehavior.Strict);
        var defaults = new GovernanceDefaultsConfig
        {
            AllowedTools      = new List<string>(),
            TriageEnabled     = false,
            TokenBudget       = null,
            SessionTtlMinutes = 30
        };

        store.Setup(s => s.UpsertAsync(tenantId, It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var sut    = new GovernanceDefaultsBaselineSeeder(store.Object, Options(defaults));
        var seeded = await sut.SeedAsync(tenantId, null);

        Assert.DoesNotContain("TokenBudget", seeded);
        Assert.Contains("AllowedTools",      seeded);
        Assert.Contains("TriageEnabled",     seeded);
        Assert.Contains("SessionTtlMinutes", seeded);
    }

    [Fact]
    public async Task SeedAsync_UpsertsCalled_ForEachExpectedKey()
    {
        var tenantId = Guid.NewGuid();
        var store    = new Mock<ITenantConfigStore>(MockBehavior.Strict);
        var defaults = new GovernanceDefaultsConfig
        {
            AllowedTools      = new List<string> { "deploy" },
            TriageEnabled     = true,
            TokenBudget       = 2000,
            SessionTtlMinutes = 45
        };

        var upsertedKeys = new List<string>();
        store.Setup(s => s.UpsertAsync(tenantId, Capture.In(upsertedKeys), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var sut = new GovernanceDefaultsBaselineSeeder(store.Object, Options(defaults));
        await sut.SeedAsync(tenantId, "system");

        Assert.Contains("AllowedTools",      upsertedKeys);
        Assert.Contains("TriageEnabled",     upsertedKeys);
        Assert.Contains("TokenBudget",       upsertedKeys);
        Assert.Contains("SessionTtlMinutes", upsertedKeys);
        Assert.Equal(4, upsertedKeys.Count);
    }

    [Fact]
    public async Task SeedAsync_ReturnsSeededKeyNames_AsReadOnlyList()
    {
        var tenantId = Guid.NewGuid();
        var store    = new Mock<ITenantConfigStore>(MockBehavior.Strict);
        var defaults = new GovernanceDefaultsConfig
        {
            AllowedTools      = new List<string>(),
            TriageEnabled     = false,
            TokenBudget       = null,
            SessionTtlMinutes = 30
        };

        store.Setup(s => s.UpsertAsync(tenantId, It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var sut    = new GovernanceDefaultsBaselineSeeder(store.Object, Options(defaults));
        var result = await sut.SeedAsync(tenantId, null);

        Assert.IsAssignableFrom<IReadOnlyList<string>>(result);
        Assert.Equal(3, result.Count); // AllowedTools, TriageEnabled, SessionTtlMinutes
    }
}

// ─── TenantOnboardingOrchestrator — Extended Steps (§6.19) ───────────────────

public sealed class OnboardingOrchestratorExtendedStepsTests
{
    private static Tenant MakeActiveTenant(Guid id)
    {
        var tenant = Tenant.Create("Contoso", null);
        typeof(Tenant).GetProperty(nameof(Tenant.TenantId))!.SetValue(tenant, id);
        return tenant;
    }

    private static TenantOnboardingOrchestrator BuildSut(
        Guid tenantId,
        Mock<ITenantRegistry> registry,
        IResourceDiscoveryService? discovery       = null,
        IConnectorHealthValidator? connectorHealth = null,
        IOnboardingBaselineSeeder? seeder          = null)
        => new(registry.Object,
               NullLogger<TenantOnboardingOrchestrator>.Instance,
               discovery,
               connectorHealth,
               seeder);

    [Fact]
    public async Task OnboardAsync_AddsResourceDiscoveryStep_WhenDiscoveryServiceWired()
    {
        var tenantId = Guid.NewGuid();
        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeActiveTenant(tenantId));

        var discovery = new Mock<IResourceDiscoveryService>(MockBehavior.Strict);
        discovery.Setup(d => d.DiscoverAsync(tenantId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new ResourceDiscoverySummary(tenantId, 3, new[] { "arm" }));

        var sut    = BuildSut(tenantId, registry, discovery: discovery.Object);
        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId));

        Assert.Equal(OnboardingStatus.Completed, result.Status);
        Assert.Contains("ResourceDiscovery", result.CompletedSteps);
    }

    [Fact]
    public async Task OnboardAsync_AddsConnectorHealthValidationStep_WhenValidatorWired()
    {
        var tenantId  = Guid.NewGuid();
        var registry  = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeActiveTenant(tenantId));

        var validator = new Mock<IConnectorHealthValidator>(MockBehavior.Strict);
        validator.Setup(v => v.ValidateConnectorsAsync(tenantId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Array.Empty<string>());

        var sut    = BuildSut(tenantId, registry, connectorHealth: validator.Object);
        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId));

        Assert.Contains("ConnectorHealthValidation", result.CompletedSteps);
    }

    [Fact]
    public async Task OnboardAsync_AddsSeedingStep_WhenBaselineSeederWired()
    {
        var tenantId = Guid.NewGuid();
        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeActiveTenant(tenantId));

        var seeder = new Mock<IOnboardingBaselineSeeder>(MockBehavior.Strict);
        seeder.Setup(s => s.SeedAsync(tenantId, null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "AllowedTools", "TriageEnabled" });

        var sut    = BuildSut(tenantId, registry, seeder: seeder.Object);
        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId));

        Assert.Contains("BaselineSeeding", result.CompletedSteps);
    }

    [Fact]
    public async Task OnboardAsync_AllFiveSteps_WhenAllServicesWired()
    {
        var tenantId = Guid.NewGuid();
        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeActiveTenant(tenantId));

        var discovery = new Mock<IResourceDiscoveryService>(MockBehavior.Strict);
        discovery.Setup(d => d.DiscoverAsync(tenantId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new ResourceDiscoverySummary(tenantId, 0, Array.Empty<string>()));

        var validator = new Mock<IConnectorHealthValidator>(MockBehavior.Strict);
        validator.Setup(v => v.ValidateConnectorsAsync(tenantId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Array.Empty<string>());

        var seeder = new Mock<IOnboardingBaselineSeeder>(MockBehavior.Strict);
        seeder.Setup(s => s.SeedAsync(tenantId, "admin", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "AllowedTools" });

        var sut = BuildSut(tenantId, registry, discovery.Object, validator.Object, seeder.Object);
        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId, "admin"));

        Assert.Equal(OnboardingStatus.Completed, result.Status);
        Assert.Equal(5, result.CompletedSteps.Count);
        Assert.Equal("TenantLookup",              result.CompletedSteps[0]);
        Assert.Equal("ActivationCheck",           result.CompletedSteps[1]);
        Assert.Equal("ResourceDiscovery",         result.CompletedSteps[2]);
        Assert.Equal("ConnectorHealthValidation", result.CompletedSteps[3]);
        Assert.Equal("BaselineSeeding",           result.CompletedSteps[4]);
    }

    [Fact]
    public async Task OnboardAsync_ReturnsFailed_WhenConnectorValidationFails()
    {
        var tenantId = Guid.NewGuid();
        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeActiveTenant(tenantId));

        var validator = new Mock<IConnectorHealthValidator>(MockBehavior.Strict);
        validator.Setup(v => v.ValidateConnectorsAsync(tenantId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new[] { "azure-monitor", "azure-devops" });

        var sut    = BuildSut(tenantId, registry, connectorHealth: validator.Object);
        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId));

        Assert.Equal(OnboardingStatus.Failed, result.Status);
        Assert.Equal("ConnectorHealthValidation", result.FailedStep);
        Assert.Contains("azure-monitor",  result.ErrorMessage);
        Assert.Contains("azure-devops",   result.ErrorMessage);
        Assert.DoesNotContain("BaselineSeeding", result.CompletedSteps);
    }

    [Fact]
    public async Task OnboardAsync_StepsExcludeOptionalOnes_WhenNullServicesInjected()
    {
        var tenantId = Guid.NewGuid();
        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeActiveTenant(tenantId));

        // No optional services — same as pre-§6.19 behaviour
        var sut    = BuildSut(tenantId, registry);
        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId));

        Assert.Equal(2, result.CompletedSteps.Count);
        Assert.DoesNotContain("ResourceDiscovery",         result.CompletedSteps);
        Assert.DoesNotContain("ConnectorHealthValidation", result.CompletedSteps);
        Assert.DoesNotContain("BaselineSeeding",           result.CompletedSteps);
    }

    [Fact]
    public async Task OnboardAsync_ConnectorHealthStep_OccursBefore_BaselineSeeding()
    {
        var tenantId = Guid.NewGuid();
        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeActiveTenant(tenantId));

        var validator = new Mock<IConnectorHealthValidator>(MockBehavior.Strict);
        validator.Setup(v => v.ValidateConnectorsAsync(tenantId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Array.Empty<string>());

        var seeder = new Mock<IOnboardingBaselineSeeder>(MockBehavior.Strict);
        seeder.Setup(s => s.SeedAsync(tenantId, null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "AllowedTools" });

        var sut    = BuildSut(tenantId, registry, connectorHealth: validator.Object, seeder: seeder.Object);
        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId));

        var connIdx  = result.CompletedSteps.ToList().IndexOf("ConnectorHealthValidation");
        var seedIdx  = result.CompletedSteps.ToList().IndexOf("BaselineSeeding");
        Assert.True(connIdx < seedIdx, "ConnectorHealthValidation must precede BaselineSeeding");
    }
}
