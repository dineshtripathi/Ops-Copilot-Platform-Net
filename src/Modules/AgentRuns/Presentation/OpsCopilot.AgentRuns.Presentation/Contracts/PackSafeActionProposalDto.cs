namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>
/// DTO representing a safe-action proposal discovered from a pack.
/// This is a recommendation only — no execution or approval has occurred.
/// </summary>
public sealed record PackSafeActionProposalDto(
    string PackName,
    string ActionId,
    string DisplayName,
    string ActionType,
    string RequiresMode,
    string? DefinitionFile,
    string? ParametersJson,
    string? ErrorMessage,
    bool IsExecutableNow,
    string? ExecutionBlockedReason);
