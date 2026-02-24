namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Port for executing safe actions and their rollbacks.
/// Infrastructure implementations connect to the actual target systems
/// (e.g., Kubernetes, Azure Resource Manager).
/// </summary>
public interface IActionExecutor
{
    /// <summary>Executes the proposed action.</summary>
    Task<ActionExecutionResult> ExecuteAsync(
        string actionType, string payloadJson, CancellationToken ct = default);

    /// <summary>Executes a rollback for a previously executed action.</summary>
    Task<ActionExecutionResult> RollbackAsync(
        string actionType, string rollbackPayloadJson, CancellationToken ct = default);
}
