using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Services;

namespace OpsCopilot.Governance.Application.Policies;

/// <summary>
/// Config-driven token budget check. Always allows unless a budget is configured
/// and the run would exceed it. Skeleton implementation — actual token tracking
/// will be added in a future slice.
/// Uses <see cref="ITenantAwareGovernanceOptionsResolver"/> for tenant-aware resolution.
/// </summary>
public sealed class DefaultTokenBudgetPolicy : ITokenBudgetPolicy
{
    private readonly ITenantAwareGovernanceOptionsResolver _resolver;

    public DefaultTokenBudgetPolicy(ITenantAwareGovernanceOptionsResolver resolver)
    {
        _resolver = resolver;
    }

    public BudgetDecision CheckRunBudget(string tenantId, Guid runId)
    {
        var resolved = _resolver.Resolve(tenantId);

        // No budget configured = unlimited
        if (resolved.TokenBudget is null)
            return BudgetDecision.Allow();

        // Skeleton: always allow but pass the cap forward.
        // Future slices will track actual token usage per run.
        return BudgetDecision.Allow(resolved.TokenBudget);
    }
}
