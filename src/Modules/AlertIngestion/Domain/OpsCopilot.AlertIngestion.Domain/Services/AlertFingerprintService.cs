using System.Security.Cryptography;
using System.Text;

namespace OpsCopilot.AlertIngestion.Domain.Services;

/// <summary>
/// Produces a stable, deterministic fingerprint for an alert payload.
///
/// Algorithm: SHA-256 of the UTF-8 encoded JSON string, returned as an
/// upper-case hex string (64 characters).  The same raw JSON always yields
/// the same fingerprint, regardless of environment or runtime.
/// </summary>
public static class AlertFingerprintService
{
    /// <summary>
    /// Computes a SHA-256 hex fingerprint of <paramref name="rawAlertJson"/>.
    /// </summary>
    /// <param name="rawAlertJson">Raw JSON payload received from the caller.</param>
    /// <returns>64-character upper-case hex string.</returns>
    /// <exception cref="ArgumentException">
    ///   Thrown when <paramref name="rawAlertJson"/> is null or empty.
    /// </exception>
    public static string Compute(string rawAlertJson)
    {
        if (string.IsNullOrEmpty(rawAlertJson))
            throw new ArgumentException(
                "Alert JSON payload must not be null or empty.", nameof(rawAlertJson));

        var bytes = Encoding.UTF8.GetBytes(rawAlertJson);
        var hash  = SHA256.HashData(bytes);
        return Convert.ToHexString(hash); // upper-case, 64 chars
    }
}
