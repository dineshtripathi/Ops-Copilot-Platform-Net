using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Governance.Application.Policies;

/// <summary>
/// Maps runtime exceptions to deterministic degraded-mode outcomes.
/// Classifies by exception type — no stack traces leak to callers.
/// </summary>
public sealed class DefaultDegradedModePolicy : IDegradedModePolicy
{
    public DegradedDecision MapFailure(Exception ex) => ex switch
    {
        TaskCanceledException or OperationCanceledException
            => new DegradedDecision(
                IsDegraded: true,
                ErrorCode: "TOOL_TIMEOUT",
                UserMessage: "The tool call timed out. Partial results may be available.",
                Retryable: true),

        UnauthorizedAccessException
            => new DegradedDecision(
                IsDegraded: false,
                ErrorCode: "AUTH_FAILURE",
                UserMessage: "Authentication failed while executing the tool.",
                Retryable: false),

        ArgumentException or FormatException
            => new DegradedDecision(
                IsDegraded: false,
                ErrorCode: "TOOL_INPUT_INVALID",
                UserMessage: "The tool request was invalid.",
                Retryable: false),

        HttpRequestException httpEx when httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            => new DegradedDecision(
                IsDegraded: true,
                ErrorCode: "RATE_LIMITED",
                UserMessage: "The tool service is rate-limiting requests. Try again later.",
                Retryable: true),

        HttpRequestException
            => new DegradedDecision(
                IsDegraded: true,
                ErrorCode: "TOOL_HTTP_ERROR",
                UserMessage: "The tool call failed due to a network or service error.",
                Retryable: true),

        _ => new DegradedDecision(
                IsDegraded: true,
                ErrorCode: "UNKNOWN_FAILURE",
                UserMessage: "An unexpected error occurred during the tool call.",
                Retryable: false)
    };
}
