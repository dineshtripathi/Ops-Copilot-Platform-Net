namespace OpsCopilot.Tenancy.Application.DTOs;

public sealed record OnboardingRequest(Guid TenantId, string? RequestedBy = null);
