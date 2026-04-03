using Microsoft.Extensions.Logging;
using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Connectors;

/// <summary>
/// ARM-backed action-target connector.
/// Replaces <see cref="StaticActionTargetConnector"/> with a config-driven
/// implementation whose supported action types are resolved once at startup
/// (in <c>ConnectorInfrastructureExtensions.AddConnectorsModule</c>) and
/// injected as a plain list — avoiding a hard dependency on <c>IConfiguration</c>.
/// </summary>
internal sealed class ArmResourceTargetConnector : IActionTargetConnector
{
    internal static readonly string[] DefaultActionTypes =
        ["restart-service", "scale-resource", "run-diagnostic", "toggle-feature-flag"];

    private readonly string[] _actionTypes;

    public ArmResourceTargetConnector(
        IReadOnlyList<string> actionTypes,
        ILogger<ArmResourceTargetConnector> logger)
    {
        _actionTypes = [.. actionTypes];

        logger.LogInformation(
            "ArmResourceTargetConnector initialised with {Count} action types: {Types}",
            _actionTypes.Length,
            string.Join(", ", _actionTypes));

        Descriptor = new ConnectorDescriptor(
            "arm-resource-target",
            ConnectorKind.ActionTarget,
            "Azure Resource Manager action-target connector (config-driven action types)",
            _actionTypes);
    }

    public ConnectorDescriptor Descriptor { get; }

    public IReadOnlyList<string> SupportedActionTypes => _actionTypes;

    public bool SupportsActionType(string actionType) =>
        _actionTypes.Contains(actionType, StringComparer.OrdinalIgnoreCase);
}
