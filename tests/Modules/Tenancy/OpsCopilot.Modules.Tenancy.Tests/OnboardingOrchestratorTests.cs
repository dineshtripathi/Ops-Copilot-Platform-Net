using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.DTOs;
using OpsCopilot.Tenancy.Domain.Entities;
using OpsCopilot.Tenancy.Domain.Enums;
using OpsCopilot.Tenancy.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.Tenancy.Tests;

public sealed class OnboardingOrchestratorTests
{
    private static Tenant MakeActiveTenant(Guid id)
    {
        var tenant = Tenant.Create("Contoso", null);
        typeof(Tenant).GetProperty(nameof(Tenant.TenantId))!.SetValue(tenant, id);
        return tenant;
    }

    private static Tenant MakeInactiveTenant(Guid id)
    {
        var tenant = Tenant.Create("Deactivated", null);
        typeof(Tenant).GetProperty(nameof(Tenant.TenantId))!.SetValue(tenant, id);
        // Use Deactivate() if available, else use reflection to set IsActive = false
        var deactivate = typeof(Tenant).GetMethod("Deactivate");
        if (deactivate is not null)
            deactivate.Invoke(tenant, new object?[] { null });
        else
            typeof(Tenant).GetProperty(nameof(Tenant.IsActive))!.SetValue(tenant, false);
        return tenant;
    }

    [Fact]
    public async Task OnboardAsync_ReturnsCompleted_WhenTenantExistsAndIsActive()
    {
        var tenantId = Guid.NewGuid();
        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeActiveTenant(tenantId));

        var sut = new TenantOnboardingOrchestrator(registry.Object, NullLogger<TenantOnboardingOrchestrator>.Instance);

        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId, "admin"));

        Assert.Equal(OnboardingStatus.Completed, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Contains("TenantLookup", result.CompletedSteps);
        Assert.Contains("ActivationCheck", result.CompletedSteps);
        Assert.Null(result.FailedStep);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task OnboardAsync_ReturnsFailed_WhenTenantNotFound()
    {
        var tenantId = Guid.NewGuid();
        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Tenant?)null);

        var sut = new TenantOnboardingOrchestrator(registry.Object, NullLogger<TenantOnboardingOrchestrator>.Instance);

        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId));

        Assert.Equal(OnboardingStatus.Failed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal("TenantLookup", result.FailedStep);
        Assert.NotNull(result.ErrorMessage);
        Assert.Empty(result.CompletedSteps);
    }

    [Fact]
    public async Task OnboardAsync_ReturnsFailed_WhenTenantIsInactive()
    {
        var tenantId = Guid.NewGuid();
        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeInactiveTenant(tenantId));

        var sut = new TenantOnboardingOrchestrator(registry.Object, NullLogger<TenantOnboardingOrchestrator>.Instance);

        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId, "admin"));

        Assert.Equal(OnboardingStatus.Failed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal("ActivationCheck", result.FailedStep);
        Assert.Contains("TenantLookup", result.CompletedSteps);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task OnboardAsync_CompletedSteps_ContainsBothSteps_WhenSuccess()
    {
        var tenantId = Guid.NewGuid();
        var registry = new Mock<ITenantRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeActiveTenant(tenantId));

        var sut = new TenantOnboardingOrchestrator(registry.Object, NullLogger<TenantOnboardingOrchestrator>.Instance);

        var result = await sut.OnboardAsync(new OnboardingRequest(tenantId));

        Assert.Equal(2, result.CompletedSteps.Count);
        Assert.Equal("TenantLookup", result.CompletedSteps[0]);
        Assert.Equal("ActivationCheck", result.CompletedSteps[1]);
    }
}
