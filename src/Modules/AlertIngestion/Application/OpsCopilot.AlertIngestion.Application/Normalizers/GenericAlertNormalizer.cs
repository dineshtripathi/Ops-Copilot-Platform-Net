using System.Text.Json;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.AlertIngestion.Domain.Models;

namespace OpsCopilot.AlertIngestion.Application.Normalizers;

/// <summary>
/// Fallback normalizer for unknown / custom alert providers.
/// Extracts best-effort fields from arbitrary JSON using common property names.
/// </summary>
public sealed class GenericAlertNormalizer : IAlertNormalizer
{
    public string ProviderKey => "generic";

    public bool CanHandle(string provider)
        => string.Equals(provider, ProviderKey, StringComparison.OrdinalIgnoreCase);

    public NormalizedAlert Normalize(string provider, JsonElement payload)
    {
        var alertId = TryGet(payload, "id", "alertId", "alert_id") ?? string.Empty;
        var title = TryGet(payload, "title", "name", "alertRule", "subject") ?? string.Empty;
        var description = TryGet(payload, "description", "body", "message");
        var severity = TryGet(payload, "severity", "priority", "level") ?? "Warning";
        var resourceId = TryGet(payload, "resourceId", "resource", "host", "target") ?? string.Empty;
        var sourceType = TryGet(payload, "sourceType", "source_type", "type", "source") ?? "Unknown";

        var firedAt = DateTime.UtcNow;
        var tsRaw = TryGet(payload, "firedAtUtc", "timestamp", "date", "created_at", "fired_at");
        if (tsRaw is not null && DateTime.TryParse(tsRaw, out var parsed))
            firedAt = parsed.ToUniversalTime();

        return new NormalizedAlert
        {
            Provider = provider.ToLowerInvariant(),
            AlertExternalId = alertId,
            Title = title,
            Description = description,
            Severity = severity,
            FiredAtUtc = firedAt,
            ResourceId = resourceId,
            SourceType = sourceType,
            Dimensions = null,
            RawPayload = payload.GetRawText()
        };
    }

    private static string? TryGet(JsonElement el, params string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (el.TryGetProperty(name, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }

        return null;
    }
}
