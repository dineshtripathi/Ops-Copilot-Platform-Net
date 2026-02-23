namespace OpsCopilot.BuildingBlocks.Contracts.Governance;

/// <summary>
/// Checks whether a run is within its token budget before proceeding.
/// Checked BEFORE the MCP call — over-budget runs are rejected without calling MCP.
/// </summary>
public interface ITokenBudgetPolicy
{
    BudgetDecision CheckRunBudget(string tenantId, Guid runId);
}
