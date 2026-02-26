using Microsoft.Extensions.Logging;
using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Application.Services;

/// <summary>
/// DI-driven connector registry that resolves connectors by name
/// using <see cref="StringComparer.OrdinalIgnoreCase"/> dictionaries.
/// Duplicate names within a kind are handled deterministically — last registration wins.
/// </summary>
public sealed class ConnectorRegistry : IConnectorRegistry
{
    private readonly IReadOnlyDictionary<string, IObservabilityConnector> _observability;
    private readonly IReadOnlyDictionary<string, IRunbookConnector> _runbook;
    private readonly IReadOnlyDictionary<string, IActionTargetConnector> _actionTarget;

    public ConnectorRegistry(
        IEnumerable<IObservabilityConnector> observability,
        IEnumerable<IRunbookConnector> runbook,
        IEnumerable<IActionTargetConnector> actionTarget,
        ILogger<ConnectorRegistry> logger)
    {
        _observability = BuildLookup(observability, c => c.Descriptor);
        _runbook = BuildLookup(runbook, c => c.Descriptor);
        _actionTarget = BuildLookup(actionTarget, c => c.Descriptor);

        logger.LogInformation(
            "ConnectorRegistry initialised: {ObsCount} observability, " +
            "{RunCount} runbook, {ActCount} action-target connectors",
            _observability.Count,
            _runbook.Count,
            _actionTarget.Count);
    }

    public IObservabilityConnector? GetObservabilityConnector(string name) =>
        _observability.TryGetValue(name, out var c) ? c : null;

    public IRunbookConnector? GetRunbookConnector(string name) =>
        _runbook.TryGetValue(name, out var c) ? c : null;

    public IActionTargetConnector? GetActionTargetConnector(string name) =>
        _actionTarget.TryGetValue(name, out var c) ? c : null;

    public IReadOnlyList<ConnectorDescriptor> ListAll()
    {
        var list = new List<ConnectorDescriptor>(
            _observability.Count + _runbook.Count + _actionTarget.Count);

        foreach (var c in _observability.Values) list.Add(c.Descriptor);
        foreach (var c in _runbook.Values) list.Add(c.Descriptor);
        foreach (var c in _actionTarget.Values) list.Add(c.Descriptor);

        return list;
    }

    public IReadOnlyList<ConnectorDescriptor> ListByKind(ConnectorKind kind) =>
        ListAll().Where(d => d.Kind == kind).ToList();

    // ── helpers ──────────────────────────────────────────────────────────
    private static Dictionary<string, T> BuildLookup<T>(
        IEnumerable<T> connectors,
        Func<T, ConnectorDescriptor> descriptorSelector)
    {
        // Last-wins semantics for duplicate names — deterministic, no exception.
        var dict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in connectors)
            dict[descriptorSelector(c).Name] = c;
        return dict;
    }
}
