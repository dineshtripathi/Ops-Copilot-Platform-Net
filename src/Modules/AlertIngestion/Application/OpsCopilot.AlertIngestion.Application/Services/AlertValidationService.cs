namespace OpsCopilot.AlertIngestion.Application.Services;

/// <summary>
/// Deterministic validation for alert ingestion.
/// Reason codes are frozen — do NOT add or rename without a design review.
/// </summary>
public static class AlertValidationService
{
    // ── Frozen reason-code / message pairs ─────────────────────────
    public const string UnsupportedProviderCode = "unsupported_provider";
    public const string UnsupportedProviderMessage = "The specified provider is not supported.";

    public const string InvalidAlertPayloadCode = "invalid_alert_payload";
    public const string InvalidAlertPayloadMessage = "The alert payload could not be parsed.";

    public sealed record ValidationResult(bool IsValid, string? ReasonCode = null, string? Message = null);

    public static ValidationResult ValidateProvider(string provider, AlertNormalizerRouter router)
    {
        if (string.IsNullOrWhiteSpace(provider) || !router.IsSupported(provider))
            return new ValidationResult(false, UnsupportedProviderCode, UnsupportedProviderMessage);

        return new ValidationResult(true);
    }

    public static ValidationResult ValidatePayload(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new ValidationResult(false, InvalidAlertPayloadCode, InvalidAlertPayloadMessage);

        return new ValidationResult(true);
    }
}
