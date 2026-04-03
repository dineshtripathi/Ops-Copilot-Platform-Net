namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 94: read-only networking/connectivity triage signals classified from persisted run data.
/// Deterministic text-pattern classification — no raw error text, no live network calls.
/// </summary>
public sealed record ConnectivitySignals(
    int TotalSignals,
    IReadOnlyList<ConnectivitySignal> Signals);

/// <summary>A single connectivity triage signal.</summary>
/// <param name="Category">dns | timeout | tls | refused | unreachable | gateway-path</param>
/// <param name="Summary">Deterministic label — no raw error strings, no token data.</param>
public sealed record ConnectivitySignal(string Category, string Summary);
