using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.Reporting.Infrastructure.Persistence;

namespace OpsCopilot.Reporting.Infrastructure;

/// <summary>
/// Slice 94 — classifies networking/connectivity triage signals from already-persisted
/// AgentRun data (Status + SummaryJson). Deterministic text-pattern matching only:
/// no live network calls, no Azure SDK, no raw error strings in output.
///
/// Signals are capped at <see cref="MaxSignals"/> to bound output size.
/// Exceptions are caught and logged as warnings so callers receive null (graceful degradation).
/// </summary>
internal sealed class ConnectivityEvidenceProvider(
    ReportingReadDbContext db,
    ILogger<ConnectivityEvidenceProvider> logger) : IConnectivityEvidenceProvider
{
    private const int MaxSignals = 20;

    // Each entry: (category, keywords[], deterministic summary)
    private static readonly (string Category, string[] Keywords, string Summary)[] s_patterns =
    [
        ("dns",          ["dns", "name resolution", "nxdomain", "resolv", "no such host"],
                         "DNS resolution failure detected"),
        ("timeout",      ["timeout", "timed out", "request timeout", "connection timeout", "operation timed"],
                         "Network timeout detected"),
        ("tls",          ["tls", "ssl", "certificate", "handshake", "x509", "tls handshake"],
                         "TLS/SSL error detected"),
        ("refused",      ["refused", "connection refused", "econnrefused"],
                         "Connection refused by remote endpoint"),
        ("unreachable",  ["unreachable", "no route", "network unreachable", "host not found", "icmp unreachable"],
                         "Network host or path unreachable"),
        ("gateway-path", ["gateway", "bad gateway", "502", "503", "upstream", "proxy error"],
                         "Gateway or upstream proxy issue detected"),
    ];

    public async Task<ConnectivitySignals?> GetSignalsAsync(
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

            var signals = new List<ConnectivitySignal>();

            foreach (var (category, keywords, summary) in s_patterns)
            {
                if (signals.Count >= MaxSignals)
                    break;

                if (keywords.Any(kw => corpus.Contains(kw, StringComparison.Ordinal)))
                    signals.Add(new ConnectivitySignal(category, summary));
            }

            if (signals.Count == 0)
                return null;

            return new ConnectivitySignals(signals.Count, signals);
        }
        catch (Exception ex)
        {
            // Log run identifier only — no payload, no raw error text, no token data.
            logger.LogWarning(ex,
                "Connectivity evidence collection failed for run {RunId}", runId);
            return null;
        }
    }
}
