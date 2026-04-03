namespace OpsCopilot.Reporting.Domain.Models;

// Slice 95: identity/auth failure signals — deterministic text classification, no secrets/tokens in output.
public sealed record AuthSignals(
    int TotalSignals,
    IReadOnlyList<AuthSignal> Signals);

// <param name="Category">unauthorized | forbidden | token-acquisition | managed-identity |
//   key-vault-access | secret-config | credential-chain</param>
// <param name="Summary">Deterministic label — no raw error strings, no secret data.</param>
public sealed record AuthSignal(string Category, string Summary);
