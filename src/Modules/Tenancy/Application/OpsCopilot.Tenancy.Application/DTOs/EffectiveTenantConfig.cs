namespace OpsCopilot.Tenancy.Application.DTOs;

public sealed record EffectiveTenantConfig(
    List<string> AllowedTools,
    bool TriageEnabled,
    int? TokenBudget,
    int SessionTtlMinutes);
