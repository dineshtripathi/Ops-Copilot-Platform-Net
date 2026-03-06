namespace OpsCopilot.BuildingBlocks.Contracts.Packs;

/// <summary>
/// Input for <see cref="IPackSafeActionProposer.ProposeAsync"/>.
/// </summary>
/// <param name="DeploymentMode">Current deployment mode (A/B/C).</param>
/// <param name="TenantId">Optional tenant identifier for scoping and telemetry.</param>
/// <param name="CorrelationId">Optional correlation identifier; auto-generated when null.</param>
public sealed record PackSafeActionProposalRequest(
    string DeploymentMode,
    string? TenantId = null,
    string? CorrelationId = null);

/// <summary>
/// A single safe-action proposal discovered from a pack manifest and its definition file.
/// These are recommendations only — no execution or approval has occurred.
/// </summary>
/// <param name="PackName">The pack that owns this action.</param>
/// <param name="ActionId">Unique action identifier within the pack.</param>
/// <param name="DisplayName">Human-readable action name from the definition file.</param>
/// <param name="ActionType">The type of action (e.g. run-command).</param>
/// <param name="RequiresMode">Minimum deployment mode required for this action.</param>
/// <param name="DefinitionFile">Relative path to the action definition file within the pack.</param>
/// <param name="ParametersJson">Serialised parameters from the definition file, or null on error.</param>
/// <param name="ErrorMessage">Non-null when the definition file could not be read or parsed.</param>
public sealed record PackSafeActionProposalItem(
    string PackName,
    string ActionId,
    string DisplayName,
    string ActionType,
    string RequiresMode,
    string? DefinitionFile,
    string? ParametersJson,
    string? ErrorMessage);

/// <summary>
/// Result of <see cref="IPackSafeActionProposer.ProposeAsync"/>.
/// </summary>
/// <param name="Proposals">Zero or more safe-action proposals discovered from packs.</param>
/// <param name="Errors">Any errors encountered during discovery (pack-level or action-level).</param>
public sealed record PackSafeActionProposalResult(
    IReadOnlyList<PackSafeActionProposalItem> Proposals,
    IReadOnlyList<string> Errors);
