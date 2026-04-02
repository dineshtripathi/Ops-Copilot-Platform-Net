using OpsCopilot.Tenancy.Application.DTOs;
using OpsCopilot.Tenancy.Domain.Enums;
using Xunit;

namespace OpsCopilot.Modules.Tenancy.Tests;

public sealed class OnboardingDomainTests
{
    // ── OnboardingStatus ─────────────────────────────────────────────────────

    [Fact]
    public void OnboardingStatus_AllValuesExist()
    {
        var values = Enum.GetNames<OnboardingStatus>();
        Assert.Contains("Pending",   values);
        Assert.Contains("Running",   values);
        Assert.Contains("Completed", values);
        Assert.Contains("Failed",    values);
    }

    // ── OnboardingRequest ────────────────────────────────────────────────────

    [Fact]
    public void OnboardingRequest_Properties_RoundTrip()
    {
        var tenantId = Guid.NewGuid();
        var req = new OnboardingRequest(tenantId, "operator@contoso.com");

        Assert.Equal(tenantId,                  req.TenantId);
        Assert.Equal("operator@contoso.com",    req.RequestedBy);
    }

    [Fact]
    public void OnboardingRequest_RequestedBy_IsOptional()
    {
        var req = new OnboardingRequest(Guid.NewGuid());
        Assert.Null(req.RequestedBy);
    }

    // ── OnboardingResult ─────────────────────────────────────────────────────

    [Fact]
    public void OnboardingResult_IsSuccess_WhenStatusCompleted()
    {
        var tenantId = Guid.NewGuid();
        var steps = new[] { "ConnectorHealthCheck", "BaselineGeneration" };
        var result = new OnboardingResult(tenantId, OnboardingStatus.Completed, steps);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.CompletedSteps.Count);
        Assert.Null(result.FailedStep);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void OnboardingResult_IsSuccess_FalseWhenStatusFailed()
    {
        var tenantId = Guid.NewGuid();
        var result = new OnboardingResult(
            tenantId,
            OnboardingStatus.Failed,
            Array.Empty<string>(),
            FailedStep:    "ConnectorHealthCheck",
            ErrorMessage:  "Credential not found");

        Assert.False(result.IsSuccess);
        Assert.Equal("ConnectorHealthCheck", result.FailedStep);
        Assert.Equal("Credential not found", result.ErrorMessage);
    }
}
