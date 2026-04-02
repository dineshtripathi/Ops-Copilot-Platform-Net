using OpsCopilot.Tenancy.Application.DTOs;

namespace OpsCopilot.Tenancy.Application.Abstractions;

public interface IOnboardingOrchestrator
{
    Task<OnboardingResult> OnboardAsync(OnboardingRequest request, CancellationToken ct = default);
}
