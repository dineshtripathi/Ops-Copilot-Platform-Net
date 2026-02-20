namespace OpsCopilot.AgentRuns.Domain.Enums;

public enum AgentRunStatus
{
    Pending,
    Running,
    Completed,
    Degraded,   // tool call failed; run completed with partial / error data
    Failed      // unrecoverable; run aborted
}
