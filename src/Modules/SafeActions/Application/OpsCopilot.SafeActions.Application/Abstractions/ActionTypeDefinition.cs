namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Describes a single action type in the catalog, including its risk classification
/// and whether it is currently enabled for proposal.
/// </summary>
/// <param name="ActionType">Case-insensitive action type key (e.g. "restart_pod").</param>
/// <param name="RiskTier">Risk classification for governance and display.</param>
/// <param name="Enabled">When <c>false</c>, proposals for this type are denied.</param>
public sealed record ActionTypeDefinition(
    string         ActionType,
    ActionRiskTier RiskTier,
    bool           Enabled);
