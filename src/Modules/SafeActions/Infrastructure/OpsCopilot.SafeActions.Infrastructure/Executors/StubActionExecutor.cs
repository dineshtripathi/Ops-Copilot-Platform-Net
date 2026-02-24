using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Placeholder action executor that returns success without performing real work.
/// Replace with real implementations that connect to target systems
/// (e.g., Kubernetes, Azure Resource Manager) in subsequent slices.
/// </summary>
internal sealed class StubActionExecutor : IActionExecutor
{
    public Task<ActionExecutionResult> ExecuteAsync(
        string actionType, string payloadJson, CancellationToken ct)
    {
        return Task.FromResult(new ActionExecutionResult(
            Success:      true,
            ResponseJson: "{\"status\":\"stub_executed\"}",
            DurationMs:   0));
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string actionType, string rollbackPayloadJson, CancellationToken ct)
    {
        return Task.FromResult(new ActionExecutionResult(
            Success:      true,
            ResponseJson: "{\"status\":\"stub_rolled_back\"}",
            DurationMs:   0));
    }
}
