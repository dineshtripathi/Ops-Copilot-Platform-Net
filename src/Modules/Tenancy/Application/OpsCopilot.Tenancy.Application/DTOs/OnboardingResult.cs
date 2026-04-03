using OpsCopilot.Tenancy.Domain.Enums;

namespace OpsCopilot.Tenancy.Application.DTOs;

public sealed record OnboardingResult(
    Guid TenantId,
    OnboardingStatus Status,
    IReadOnlyList<string> CompletedSteps,
    string? FailedStep = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == OnboardingStatus.Completed;
}
