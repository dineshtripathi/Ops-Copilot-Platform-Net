using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure.Persistence;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// Slice 95 — classifies identity/auth failure signals from already-persisted
/// AgentRun data (Status + SummaryJson). Deterministic text-pattern matching only:
/// no live auth calls, no Azure SDK, no secrets/tokens in output.
///
/// Signals are capped at <see cref="MaxSignals"/> to bound output size.
/// Exceptions are caught and logged as warnings so callers receive null (graceful degradation).
/// </summary>
internal sealed class AuthEvidenceProvider(
    ReportingReadDbContext db,
    ILogger<AuthEvidenceProvider> logger) : IAuthEvidenceProvider
{
    private const int MaxSignals = 20;

    // Each entry: (category, keywords[], deterministic summary)
    private static readonly (string Category, string[] Keywords, string Summary)[] s_patterns =
    [
        ("unauthorized",
         ["401", "unauthorized", "authentication failed", "invalid issuer", "invalid audience"],
         "Authentication failure or invalid credentials detected"),
        ("forbidden",
         ["403", "forbidden", "access denied", "not authorized"],
         "Authorization denied — insufficient permissions"),
        ("managed-identity",
         ["managed identity", "managedidentitycredential", "msi endpoint", "imds"],
         "Managed identity credential failure detected"),
        ("token-acquisition",
         ["token acquisition", "failed to acquire token", "acquire token", "token request failed"],
         "Token acquisition failure detected"),
        ("credential-chain",
         ["defaultazurecredential", "credential unavailable", "credential chain", "no credentials"],
         "Credential chain exhausted or unavailable"),
        ("key-vault-access",
         ["key vault", "keyvault", "secret not found", "vault not found", "permission denied to secrets"],
         "Key Vault access failure detected"),
        ("secret-config",
         ["secret", "configuration auth", "authentication configuration", "missing secret"],
         "Secret or authentication configuration issue detected"),
    ];

    public async Task<AuthSignals?> GetSignalsAsync(
        Guid runId, string tenantId, CancellationToken ct)
    {
        try
        {
            var record = await db.AgentRunRecords
                .FirstOrDefaultAsync(r => r.RunId == runId && r.TenantId == tenantId, ct);

            if (record is null)
                return null;

            // Build a single lower-case searchable corpus from non-sensitive persisted fields.
            // Status and SummaryJson contain structured classification text only — not raw payloads.
            var corpus = string.Concat(
                record.Status ?? string.Empty, " ",
                record.SummaryJson ?? string.Empty)
                .ToLowerInvariant();

            var signals = new List<AuthSignal>();

            foreach (var (category, keywords, summary) in s_patterns)
            {
                if (signals.Count >= MaxSignals)
                    break;

                if (keywords.Any(kw => corpus.Contains(kw, StringComparison.Ordinal)))
                    signals.Add(new AuthSignal(category, summary));
            }

            if (signals.Count == 0)
                return null;

            return new AuthSignals(signals.Count, signals);
        }
        catch (Exception ex)
        {
            // Log run identifier only — no payload, no raw error text, no token data.
            logger.LogWarning(ex,
                "Auth evidence collection failed for run {RunId}", runId);
            return null;
        }
    }
}
