using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Connectors;

/// <summary>
/// Static action-target metadata connector that returns supported action types
/// from in-memory definitions. No external I/O — pure metadata.
/// </summary>
public sealed class StaticActionTargetConnector : IActionTargetConnector
{
    private static readonly string[] ActionTypes =
        ["restart-service", "scale-resource", "run-diagnostic", "toggle-feature-flag"];

    public ConnectorDescriptor Descriptor { get; } = new(
        "static-action-target",
        ConnectorKind.ActionTarget,
        "Static action target metadata from in-memory definitions",
        ActionTypes);

    public IReadOnlyList<string> SupportedActionTypes => ActionTypes;

    public bool SupportsActionType(string actionType) =>
        SupportedActionTypes.Contains(actionType, StringComparer.OrdinalIgnoreCase);
}
