using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Policies;

/// <summary>
/// Bridges SafeActions to Governance module contracts.
/// Delegates tool-allowlist and token-budget checks to the
/// <see cref="IToolAllowlistPolicy"/> and <see cref="ITokenBudgetPolicy"/>
/// implementations registered by the Governance module.
/// </summary>
public sealed class GovernancePolicyClient : IGovernancePolicyClient
{
    private readonly IToolAllowlistPolicy _toolPolicy;
    private readonly ITokenBudgetPolicy   _budgetPolicy;

    public GovernancePolicyClient(
        IToolAllowlistPolicy toolPolicy,
        ITokenBudgetPolicy   budgetPolicy)
    {
        _toolPolicy   = toolPolicy;
        _budgetPolicy = budgetPolicy;
    }

    /// <inheritdoc />
    public PolicyDecision EvaluateToolAllowlist(string tenantId, string actionType)
        => _toolPolicy.CanUseTool(tenantId, actionType);

    /// <inheritdoc />
    public BudgetDecision EvaluateTokenBudget(string tenantId, string actionType, int? requestedTokens = null)
    {
        // Delegate to the budget policy using a deterministic run-id derived from
        // the actionType so the policy can correlate.  The current DefaultTokenBudgetPolicy
        // implementation ignores runId and checks only the tenant's budget cap,
        // so a deterministic GUID is safe and forward-compatible.
        var syntheticRunId = DeterministicGuid(tenantId, actionType);
        return _budgetPolicy.CheckRunBudget(tenantId, syntheticRunId);
    }

    /// <summary>
    /// Produces a stable GUID from tenant + actionType so repeated calls
    /// for the same pair yield the same run-id.
    /// </summary>
    private static Guid DeterministicGuid(string tenantId, string actionType)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{tenantId}::{actionType}"));
        // Use the first 16 bytes of the SHA-256 hash as a GUID
        return new Guid(bytes.AsSpan(0, 16));
    }
}
