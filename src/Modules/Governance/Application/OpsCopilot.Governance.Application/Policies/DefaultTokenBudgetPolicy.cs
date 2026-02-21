using Microsoft.Extensions.Options;
using OpsCopilot.Governance.Application.Configuration;
using OpsCopilot.Governance.Application.Models;

namespace OpsCopilot.Governance.Application.Policies;

/// <summary>
/// Config-driven token budget check. Always allows unless a budget is configured
/// and the run would exceed it. Skeleton implementation — actual token tracking
/// will be added in a future slice.
/// </summary>
public sealed class DefaultTokenBudgetPolicy : ITokenBudgetPolicy
{
    private readonly GovernanceOptions _options;

    public DefaultTokenBudgetPolicy(IOptions<GovernanceOptions> options)
    {
        _options = options.Value;
    }

    public BudgetDecision CheckRunBudget(string tenantId, Guid runId)
    {
        var maxTokens = ResolveTokenBudget(tenantId);

        // No budget configured = unlimited
        if (maxTokens is null)
            return BudgetDecision.Allow();

        // Skeleton: always allow but pass the cap forward.
        // Future slices will track actual token usage per run.
        return BudgetDecision.Allow(maxTokens);
    }

    private int? ResolveTokenBudget(string tenantId)
    {
        if (_options.TenantOverrides.TryGetValue(tenantId, out var tenantOverride)
            && tenantOverride.TokenBudget is not null)
        {
            return tenantOverride.TokenBudget;
        }

        return _options.Defaults.TokenBudget;
    }
}
