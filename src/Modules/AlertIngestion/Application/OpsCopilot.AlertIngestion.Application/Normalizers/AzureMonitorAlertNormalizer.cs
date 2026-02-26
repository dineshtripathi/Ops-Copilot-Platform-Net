using System.Text.Json;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.AlertIngestion.Domain.Models;

namespace OpsCopilot.AlertIngestion.Application.Normalizers;

/// <summary>
/// Normalizes Azure Monitor alert payloads into <see cref="NormalizedAlert"/>.
/// Expected JSON shape mirrors the Azure Monitor common alert schema.
/// </summary>
public sealed class AzureMonitorAlertNormalizer : IAlertNormalizer
{
    public string ProviderKey => "azure_monitor";

    public bool CanHandle(string provider)
        => string.Equals(provider, ProviderKey, StringComparison.OrdinalIgnoreCase);

    public NormalizedAlert Normalize(string provider, JsonElement payload)
    {
        var data = payload.GetProperty("data");
        var essentials = data.GetProperty("essentials");

        var alertId = essentials.GetProperty("alertId").GetString() ?? string.Empty;
        var alertRule = essentials.GetProperty("alertRule").GetString() ?? string.Empty;
        var severity = essentials.GetProperty("severity").GetString() ?? "Unknown";
        var firedDateTime = essentials.GetProperty("firedDateTime").GetString() ?? DateTime.UtcNow.ToString("O");
        var description = essentials.TryGetProperty("description", out var desc)
            ? desc.GetString()
            : null;

        // Target resource ID — first element of targetResourceIds array
        var resourceId = string.Empty;
        if (essentials.TryGetProperty("targetResourceIds", out var ids) &&
            ids.ValueKind == JsonValueKind.Array &&
            ids.GetArrayLength() > 0)
        {
            resourceId = ids[0].GetString() ?? string.Empty;
        }

        var sourceType = essentials.TryGetProperty("monitoringService", out var ms)
            ? ms.GetString() ?? "Metric"
            : "Metric";

        var dimensions = new Dictionary<string, string>();
        if (essentials.TryGetProperty("targetResourceType", out var trt))
            dimensions["targetResourceType"] = trt.GetString() ?? string.Empty;
        if (essentials.TryGetProperty("monitorCondition", out var mc))
            dimensions["monitorCondition"] = mc.GetString() ?? string.Empty;

        return new NormalizedAlert
        {
            Provider = ProviderKey,
            AlertExternalId = alertId,
            Title = alertRule,
            Description = description,
            Severity = NormalizeSeverity(severity),
            FiredAtUtc = DateTime.TryParse(firedDateTime, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow,
            ResourceId = resourceId,
            SourceType = sourceType,
            Dimensions = dimensions.Count > 0 ? dimensions : null,
            RawPayload = payload.GetRawText()
        };
    }

    private static string NormalizeSeverity(string severity)
        => severity switch
        {
            "Sev0" => "Critical",
            "Sev1" => "Error",
            "Sev2" => "Warning",
            "Sev3" => "Informational",
            "Sev4" => "Informational",
            _ => severity
        };
}
