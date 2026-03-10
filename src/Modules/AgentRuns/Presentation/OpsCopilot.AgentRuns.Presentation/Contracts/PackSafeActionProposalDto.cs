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
    string? ExecutionBlockedReason,
    bool? GovernanceAllowed = null,
    string? GovernanceReasonCode = null,
    string? GovernanceMessage = null,
    bool? ScopeAllowed = null,
    string? ScopeReasonCode = null,
    string? ScopeMessage = null,
    string? DefinitionValidationErrorCode = null,
    string? DefinitionValidationMessage = null,
    string? OperatorPreview = null);
