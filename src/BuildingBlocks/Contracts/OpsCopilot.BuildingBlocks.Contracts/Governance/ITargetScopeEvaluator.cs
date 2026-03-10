namespace OpsCopilot.BuildingBlocks.Contracts.Governance;

/// <summary>
/// Evaluates whether a target scope (Azure subscription, Log Analytics workspace)
/// is allowed for a tenant.
/// </summary>
public interface ITargetScopeEvaluator
{
    TargetScopeDecision Evaluate(string tenantId, string targetType, string targetValue);
}
