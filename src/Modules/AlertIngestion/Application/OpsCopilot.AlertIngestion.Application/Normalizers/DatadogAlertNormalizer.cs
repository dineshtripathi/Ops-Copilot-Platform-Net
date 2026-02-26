using System.Text.Json;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.AlertIngestion.Domain.Models;

namespace OpsCopilot.AlertIngestion.Application.Normalizers;

/// <summary>
/// Normalizes Datadog alert / monitor payloads into <see cref="NormalizedAlert"/>.
/// Expected JSON shape mirrors Datadog webhook notification payloads.
/// </summary>
public sealed class DatadogAlertNormalizer : IAlertNormalizer
{
    public string ProviderKey => "datadog";

    public bool CanHandle(string provider)
        => string.Equals(provider, ProviderKey, StringComparison.OrdinalIgnoreCase);

    public NormalizedAlert Normalize(string provider, JsonElement payload)
    {
        var alertId = payload.TryGetProperty("id", out var id)
            ? id.GetString() ?? id.ToString()
            : string.Empty;

        var title = payload.TryGetProperty("title", out var t)
            ? t.GetString() ?? string.Empty
            : string.Empty;

        var description = payload.TryGetProperty("body", out var b)
            ? b.GetString()
            : null;

        var severity = payload.TryGetProperty("priority", out var p)
            ? NormalizePriority(p.GetString() ?? "normal")
            : "Warning";

        var firedAt = DateTime.UtcNow;
        if (payload.TryGetProperty("date_happened", out var dh))
        {
            if (dh.ValueKind == JsonValueKind.Number)
                firedAt = DateTimeOffset.FromUnixTimeSeconds(dh.GetInt64()).UtcDateTime;
            else if (dh.ValueKind == JsonValueKind.String &&
                     DateTime.TryParse(dh.GetString(), out var parsed))
                firedAt = parsed.ToUniversalTime();
        }

        var resourceId = payload.TryGetProperty("host", out var h)
            ? h.GetString() ?? string.Empty
            : string.Empty;

        var sourceType = payload.TryGetProperty("alert_type", out var at)
            ? at.GetString() ?? "Event"
            : "Event";

        var dimensions = new Dictionary<string, string>();
        if (payload.TryGetProperty("tags", out var tags) &&
            tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                var tv = tag.GetString();
                if (tv is not null && tv.Contains(':'))
                {
                    var parts = tv.Split(':', 2);
                    dimensions[parts[0]] = parts[1];
                }
            }
        }

        return new NormalizedAlert
        {
            Provider = ProviderKey,
            AlertExternalId = alertId,
            Title = title,
            Description = description,
            Severity = severity,
            FiredAtUtc = firedAt,
            ResourceId = resourceId,
            SourceType = sourceType,
            Dimensions = dimensions.Count > 0 ? dimensions : null,
            RawPayload = payload.GetRawText()
        };
    }

    private static string NormalizePriority(string priority)
        => priority.ToLowerInvariant() switch
        {
            "p1" or "critical" => "Critical",
            "p2" or "high" => "Error",
            "p3" or "normal" => "Warning",
            "p4" or "low" => "Informational",
            _ => "Warning"
        };
}
